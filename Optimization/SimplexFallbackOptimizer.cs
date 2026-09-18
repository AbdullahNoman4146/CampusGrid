using CampusGrid.Models.Domain;
using CampusGrid.Models.Dto;
using CampusGrid.Validation;

namespace CampusGrid.Optimization;

/// <summary>
/// Pure C# Linear Programming solver based on Two-Phase Simplex with upper-bounded variables.
/// Provides a 100% managed zero-dependency fallback if native OR-Tools solver is unavailable.
/// </summary>
public class SimplexFallbackOptimizer
{
    public OptimizationResult Optimize(
        EnergyRequest request,
        double[] effectiveSolar,
        double[] minReserve,
        double[] maxGrid,
        double[] maxCharge,
        double[] maxDischarge)
    {
        // Dimensions:
        // Variables for each hour t (0..23):
        // 0: grid_t in [0, maxGrid_t]
        // 1: solar_used_t in [0, effectiveSolar_t]
        // 2: charge_t in [0, maxCharge_t]
        // 3: discharge_t in [0, maxDischarge_t]
        // 4: battery_energy_t in [minReserve_t, capacity]
        // Total vars N = 24 * 5 = 120.

        int T = 24;
        int nVars = T * 5;
        var lowerBounds = new double[nVars];
        var upperBounds = new double[nVars];
        var cost = new double[nVars];

        for (int t = 0; t < T; t++)
        {
            int baseIdx = t * 5;
            // grid_t
            lowerBounds[baseIdx + 0] = 0.0;
            upperBounds[baseIdx + 0] = maxGrid[t];
            cost[baseIdx + 0] = request.Hours[t].TariffBdtPerKwh;

            // solar_used_t
            lowerBounds[baseIdx + 1] = 0.0;
            upperBounds[baseIdx + 1] = effectiveSolar[t];
            cost[baseIdx + 1] = 0.0;

            // charge_t
            lowerBounds[baseIdx + 2] = 0.0;
            upperBounds[baseIdx + 2] = maxCharge[t];
            cost[baseIdx + 2] = 1e-5;

            // discharge_t
            lowerBounds[baseIdx + 3] = 0.0;
            upperBounds[baseIdx + 3] = maxDischarge[t];
            cost[baseIdx + 3] = 1e-5;

            // battery_energy_t
            lowerBounds[baseIdx + 4] = minReserve[t];
            upperBounds[baseIdx + 4] = request.Battery.CapacityKwh;
            cost[baseIdx + 4] = 0.0;
        }

        // Constraints:
        // 1. Energy balance for each t: grid_t + solar_used_t + discharge_t - charge_t = demand_t (24 constraints)
        // 2. Battery transition:
        //    t = 0: battery_0 - charge_0 + discharge_0 = initial_energy (1 constraint)
        //    t >= 1: battery_t - battery_{t-1} - charge_t + discharge_t = 0 (23 constraints)
        // 3. Neutrality: battery_23 = initial_energy (1 constraint)
        // Total equality constraints M = 24 + 1 + 23 + 1 = 49 constraints.

        int M = 49;
        var A = new double[M, nVars];
        var rhs = new double[M];

        // 1. Energy balance
        for (int t = 0; t < T; t++)
        {
            int row = t;
            int baseIdx = t * 5;
            A[row, baseIdx + 0] = 1.0;  // grid
            A[row, baseIdx + 1] = 1.0;  // solar_used
            A[row, baseIdx + 2] = -1.0; // - charge
            A[row, baseIdx + 3] = 1.0;  // + discharge
            rhs[row] = request.Hours[t].DemandKwh;
        }

        // 2. Transition t = 0
        int rowTrans0 = 24;
        A[rowTrans0, 0 * 5 + 4] = 1.0;  // battery_0
        A[rowTrans0, 0 * 5 + 2] = -1.0; // - charge_0
        A[rowTrans0, 0 * 5 + 3] = 1.0;  // + discharge_0
        rhs[rowTrans0] = request.Battery.InitialEnergyKwh;

        // Transition t = 1..23
        for (int t = 1; t < T; t++)
        {
            int row = 24 + t;
            int curBase = t * 5;
            int prevBase = (t - 1) * 5;
            A[row, curBase + 4] = 1.0;   // battery_t
            A[row, prevBase + 4] = -1.0; // - battery_{t-1}
            A[row, curBase + 2] = -1.0;  // - charge_t
            A[row, curBase + 3] = 1.0;   // + discharge_t
            rhs[row] = 0.0;
        }

        // 3. Neutrality
        int rowNeutrality = 48;
        A[rowNeutrality, 23 * 5 + 4] = 1.0; // battery_23
        rhs[rowNeutrality] = request.Battery.InitialEnergyKwh;

        // Shift variables so lower bounds are 0:
        // Let x_j = l_j + y_j where 0 <= y_j <= u_j - l_j.
        // A(l + y) = rhs  =>  Ay = rhs - Al
        var shiftedRhs = new double[M];
        for (int i = 0; i < M; i++)
        {
            double sumAl = 0;
            for (int j = 0; j < nVars; j++)
            {
                if (lowerBounds[j] != 0.0)
                {
                    sumAl += A[i, j] * lowerBounds[j];
                }
            }
            shiftedRhs[i] = rhs[i] - sumAl;
        }

        var shiftedUpper = new double[nVars];
        for (int j = 0; j < nVars; j++)
        {
            shiftedUpper[j] = upperBounds[j] - lowerBounds[j];
            if (shiftedUpper[j] < 0)
            {
                return new OptimizationResult
                {
                    Success = false,
                    ErrorMessage = $"Infeasible bounds for variable #{j}."
                };
            }
        }

        // Solve standard LP via Primal Simplex or Bounded Simplex
        var solver = new BoundedSimplexSolver(M, nVars, A, shiftedRhs, shiftedUpper, cost);
        if (!solver.Solve(out var ySolution))
        {
            return new OptimizationResult
            {
                Success = false,
                ErrorMessage = "The energy scenario is mathematically infeasible under given constraints."
            };
        }

        var solution = new double[nVars];
        for (int j = 0; j < nVars; j++)
        {
            solution[j] = lowerBounds[j] + ySolution[j];
        }

        return BuildOptimizationResult(request, solution);
    }

    private static OptimizationResult BuildOptimizationResult(EnergyRequest request, double[] solution)
    {
        var hourlyPlan = new List<HourlyPlanDto>();
        double totalGrid = 0;
        double totalCost = 0;
        double peakGrid = 0;

        for (int t = 0; t < 24; t++)
        {
            int baseIdx = t * 5;
            double grid = Math.Max(0.0, Math.Round(solution[baseIdx + 0], 4));
            double solar = Math.Max(0.0, Math.Round(solution[baseIdx + 1], 4));
            double charge = Math.Max(0.0, Math.Round(solution[baseIdx + 2], 4));
            double discharge = Math.Max(0.0, Math.Round(solution[baseIdx + 3], 4));
            double batteryEnergy = Math.Max(0.0, Math.Round(solution[baseIdx + 4], 4));

            string action = BatteryAction.Idle;
            double batteryKwh = 0.0;

            if (charge > 1e-3)
            {
                action = BatteryAction.Charge;
                batteryKwh = charge;
            }
            else if (discharge > 1e-3)
            {
                action = BatteryAction.Discharge;
                batteryKwh = discharge;
            }

            hourlyPlan.Add(new HourlyPlanDto
            {
                Hour = t,
                GridKwh = grid,
                SolarUsedKwh = solar,
                BatteryAction = action,
                BatteryKwh = batteryKwh,
                BatteryEnergyAfterKwh = batteryEnergy
            });

            totalGrid += grid;
            totalCost += grid * request.Hours[t].TariffBdtPerKwh;
            peakGrid = Math.Max(peakGrid, grid);
        }

        return new OptimizationResult
        {
            Success = true,
            HourlyPlan = hourlyPlan,
            TotalGridKwh = Math.Round(totalGrid, 2),
            TotalCostBdt = Math.Round(totalCost, 2),
            PeakGridKwh = Math.Round(peakGrid, 2)
        };
    }

    private class BoundedSimplexSolver
    {
        private readonly int _m;
        private readonly int _n;
        private readonly double[,] _a;
        private readonly double[] _b;
        private readonly double[] _u;
        private readonly double[] _c;

        public BoundedSimplexSolver(int m, int n, double[,] a, double[] b, double[] u, double[] c)
        {
            _m = m;
            _n = n;
            _a = a;
            _b = b;
            _u = u;
            _c = c;
        }

        public bool Solve(out double[] x)
        {
            // Only add upper bound constraints for variables with finite upper bounds
            var finiteBoundIndices = new List<int>();
            for (int j = 0; j < _n; j++)
            {
                if (!double.IsInfinity(_u[j]) && _u[j] < 1e12)
                {
                    finiteBoundIndices.Add(j);
                }
            }

            int nBounds = finiteBoundIndices.Count;
            int totalM = _m + nBounds;
            int totalN = _n + nBounds; // original vars + slack vars

            var fullA = new double[totalM, totalN];
            var fullB = new double[totalM];
            var fullC = new double[totalN];

            for (int i = 0; i < _m; i++)
            {
                for (int j = 0; j < _n; j++) fullA[i, j] = _a[i, j];
                fullB[i] = _b[i];
            }

            for (int k = 0; k < nBounds; k++)
            {
                int varIdx = finiteBoundIndices[k];
                fullA[_m + k, varIdx] = 1.0;
                fullA[_m + k, _n + k] = 1.0; // slack
                fullB[_m + k] = _u[varIdx];
            }

            for (int j = 0; j < _n; j++)
            {
                fullC[j] = _c[j];
            }

            var simplex = new StandardTwoPhaseSimplex(totalM, totalN, fullA, fullB, fullC);
            if (!simplex.Solve(out var fullX))
            {
                x = new double[_n];
                return false;
            }

            x = new double[_n];
            for (int j = 0; j < _n; j++)
            {
                double val = fullX[j];
                if (!double.IsInfinity(_u[j]))
                {
                    val = Math.Clamp(val, 0.0, _u[j]);
                }
                else
                {
                    val = Math.Max(0.0, val);
                }
                x[j] = val;
            }
            return true;
        }
    }

    private class StandardTwoPhaseSimplex
    {
        private const double Epsilon = 1e-9;
        private readonly int _m;
        private readonly int _n;
        private readonly double[,] _table;
        private readonly int[] _basis;

        public StandardTwoPhaseSimplex(int m, int n, double[,] a, double[] b, double[] c)
        {
            _m = m;
            _n = n;
            // Tableau: m + 2 rows (m constraints, phase 1 obj, phase 2 obj)
            // n + m + 1 columns (n original, m artificial, 1 RHS)
            _table = new double[m + 2, n + m + 1];
            _basis = new int[m];

            // Normalize b >= 0
            for (int i = 0; i < m; i++)
            {
                double bi = b[i];
                double sign = 1.0;
                if (bi < -Epsilon)
                {
                    sign = -1.0;
                    bi = -bi;
                }

                for (int j = 0; j < n; j++)
                {
                    _table[i, j] = a[i, j] * sign;
                }
                // Artificial variable
                _table[i, n + i] = 1.0;
                _table[i, n + m] = bi;
                _basis[i] = n + i;
            }

            // Phase 2 objective in row m
            for (int j = 0; j < n; j++)
            {
                _table[m, j] = c[j];
            }

            // Phase 1 objective in row m + 1: minimize sum of artificial variables
            // Start with W = sum_{i} a_{i} = sum_{i} (b_i - sum_{j} a_{ij} x_j)
            for (int i = 0; i < m; i++)
            {
                for (int j = 0; j <= n + m; j++)
                {
                    _table[m + 1, j] -= _table[i, j];
                }
            }
        }

        public bool Solve(out double[] x)
        {
            x = new double[_n];

            // Phase 1: drive artificial variables to zero
            if (!PivotToOptimality(objRow: _m + 1, maxCols: _n + _m))
            {
                return false;
            }

            // Check artificial sum
            if (Math.Abs(_table[_m + 1, _n + _m]) > 1e-4)
            {
                return false; // Infeasible
            }

            // Ensure artificial variables are not in basis with non-zero values
            for (int i = 0; i < _m; i++)
            {
                if (_basis[i] >= _n)
                {
                    // Attempt pivot to an original variable
                    for (int j = 0; j < _n; j++)
                    {
                        if (Math.Abs(_table[i, j]) > Epsilon)
                        {
                            Pivot(i, j);
                            break;
                        }
                    }
                }
            }

            // Phase 2: optimize original objective
            // Update phase 2 objective coefficients for current basis
            for (int i = 0; i < _m; i++)
            {
                int bCol = _basis[i];
                if (bCol < _n && Math.Abs(_table[_m, bCol]) > Epsilon)
                {
                    double factor = _table[_m, bCol];
                    for (int j = 0; j <= _n + _m; j++)
                    {
                        _table[_m, j] -= factor * _table[i, j];
                    }
                }
            }

            if (!PivotToOptimality(objRow: _m, maxCols: _n))
            {
                return false;
            }

            for (int i = 0; i < _m; i++)
            {
                if (_basis[i] < _n)
                {
                    x[_basis[i]] = Math.Max(0.0, _table[i, _n + _m]);
                }
            }

            return true;
        }

        private bool PivotToOptimality(int objRow, int maxCols)
        {
            int iterations = 0;
            int maxIterations = 2000;

            while (iterations++ < maxIterations)
            {
                // Find entering column (most negative reduced cost)
                int enterCol = -1;
                double minVal = -Epsilon;

                for (int j = 0; j < maxCols; j++)
                {
                    if (_table[objRow, j] < minVal)
                    {
                        minVal = _table[objRow, j];
                        enterCol = j;
                    }
                }

                if (enterCol == -1) return true; // Optimal

                // Minimum ratio test
                int leaveRow = -1;
                double minRatio = double.PositiveInfinity;

                for (int i = 0; i < _m; i++)
                {
                    if (_table[i, enterCol] > Epsilon)
                    {
                        double ratio = _table[i, _n + _m] / _table[i, enterCol];
                        if (ratio < minRatio)
                        {
                            minRatio = ratio;
                            leaveRow = i;
                        }
                    }
                }

                if (leaveRow == -1) return false; // Unbounded

                Pivot(leaveRow, enterCol);
            }

            return false;
        }

        private void Pivot(int row, int col)
        {
            double pivotVal = _table[row, col];
            int totalCols = _n + _m + 1;

            for (int j = 0; j < totalCols; j++)
            {
                _table[row, j] /= pivotVal;
            }

            for (int i = 0; i < _m + 2; i++)
            {
                if (i != row)
                {
                    double factor = _table[i, col];
                    if (Math.Abs(factor) > Epsilon)
                    {
                        for (int j = 0; j < totalCols; j++)
                        {
                            _table[i, j] -= factor * _table[row, j];
                        }
                    }
                }
            }

            _basis[row] = col;
        }
    }
}
