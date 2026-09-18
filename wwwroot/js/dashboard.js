/**
 * GridWise Smart Campus Energy Optimization Dashboard
 * Client Application Logic & Visualizations
 * BUP CSE Fest 2026 Hackathon
 */

let allCases = [];
let energyChart = null;
let batteryChart = null;

document.addEventListener('DOMContentLoaded', async () => {
    checkHealth();
    setInterval(checkHealth, 10000);
    await loadSampleCasesPack();
    setupEventListeners();
});

async function checkHealth() {
    const badge = document.getElementById('health-badge');
    const text = document.getElementById('health-text');
    try {
        const res = await fetch('/health');
        if (res.ok) {
            const data = await res.json();
            if (data.status === 'ok') {
                badge.style.display = 'flex';
                badge.style.background = 'rgba(16, 185, 129, 0.1)';
                badge.style.borderColor = 'rgba(16, 185, 129, 0.25)';
                text.textContent = 'API ONLINE';
                text.style.color = '#34d399';
                return;
            }
        }
        throw new Error();
    } catch (e) {
        badge.style.display = 'flex';
        badge.style.background = 'rgba(244, 63, 94, 0.1)';
        badge.style.borderColor = 'rgba(244, 63, 94, 0.25)';
        text.textContent = 'API OFFLINE';
        text.style.color = '#f43f5e';
    }
}

async function loadSampleCasesPack() {
    try {
        const res = await fetch('/data/sample_cases.json');
        if (res.ok) {
            const pack = await res.json();
            allCases = pack.cases || [];
            populateScenarioDropdown();
            if (allCases.length > 0) {
                applyScenario(allCases[0]);
                // Automatically run optimization on initial load for instant visual feedback
                runOptimization();
            }
        }
    } catch (err) {
        console.error('Failed to load sample cases pack:', err);
    }
}

function populateScenarioDropdown() {
    const select = document.getElementById('scenario-select');
    select.innerHTML = '';
    allCases.forEach((c, idx) => {
        const opt = document.createElement('option');
        opt.value = c.id;
        opt.textContent = `${c.id}: ${c.label}`;
        select.appendChild(opt);
    });
}

function setupEventListeners() {
    const select = document.getElementById('scenario-select');
    select.addEventListener('change', (e) => {
        const selected = allCases.find(c => c.id === e.target.value);
        if (selected) {
            applyScenario(selected);
            runOptimization();
        }
    });

    const form = document.getElementById('optimization-form');
    form.addEventListener('submit', (e) => {
        e.preventDefault();
        runOptimization();
    });

    document.getElementById('btn-copy-json').addEventListener('click', () => {
        const jsonText = document.getElementById('json-output').textContent;
        navigator.clipboard.writeText(jsonText).then(() => {
            const btn = document.getElementById('btn-copy-json');
            const orig = btn.textContent;
            btn.textContent = '✓ Copied!';
            setTimeout(() => btn.textContent = orig, 2000);
        });
    });
}

function applyScenario(caseData) {
    const inp = caseData.input;
    document.getElementById('scenario-id').value = inp.scenario_id;
    document.getElementById('operator-notes').value = (inp.operator_notes || []).join('\n');

    const b = inp.battery;
    document.getElementById('battery-capacity').value = b.capacity_kwh;
    document.getElementById('battery-initial').value = b.initial_energy_kwh;
    document.getElementById('battery-min').value = b.minimum_energy_kwh;
    document.getElementById('battery-charge-max').value = b.max_charge_kwh_per_hour;
    document.getElementById('battery-discharge-max').value = b.max_discharge_kwh_per_hour;

    renderHourlyForecastInputs(inp.hours || []);
}

function renderHourlyForecastInputs(hours) {
    const tbody = document.getElementById('forecast-tbody');
    tbody.innerHTML = '';
    hours.forEach(h => {
        const tr = document.createElement('tr');
        tr.innerHTML = `
            <td><strong>Hour ${h.hour.toString().padStart(2, '0')}:00</strong></td>
            <td><input type="number" step="any" class="form-input table-inp hour-demand" data-hour="${h.hour}" value="${h.demand_kwh}"></td>
            <td><input type="number" step="any" class="form-input table-inp hour-solar" data-hour="${h.hour}" value="${h.solar_kwh}"></td>
            <td><input type="number" step="any" class="form-input table-inp hour-tariff" data-hour="${h.hour}" value="${h.tariff_bdt_per_kwh}"></td>
        `;
        tbody.appendChild(tr);
    });
}

function buildRequestPayload() {
    const scenarioId = document.getElementById('scenario-id').value.trim() || 'CUSTOM-SCENARIO';
    const notesRaw = document.getElementById('operator-notes').value;
    const operatorNotes = notesRaw.split('\n').map(s => s.trim()).filter(s => s.length > 0);

    const battery = {
        capacity_kwh: parseFloat(document.getElementById('battery-capacity').value) || 200,
        initial_energy_kwh: parseFloat(document.getElementById('battery-initial').value) || 100,
        minimum_energy_kwh: parseFloat(document.getElementById('battery-min').value) || 40,
        max_charge_kwh_per_hour: parseFloat(document.getElementById('battery-charge-max').value) || 50,
        max_discharge_kwh_per_hour: parseFloat(document.getElementById('battery-discharge-max').value) || 50
    };

    const hours = [];
    const demandInputs = document.querySelectorAll('.hour-demand');
    const solarInputs = document.querySelectorAll('.hour-solar');
    const tariffInputs = document.querySelectorAll('.hour-tariff');

    for (let i = 0; i < demandInputs.length; i++) {
        hours.push({
            hour: i,
            demand_kwh: parseFloat(demandInputs[i].value) || 0,
            solar_kwh: parseFloat(solarInputs[i].value) || 0,
            tariff_bdt_per_kwh: parseFloat(tariffInputs[i].value) || 5
        });
    }

    return {
        scenario_id: scenarioId,
        operator_notes: operatorNotes,
        battery: battery,
        hours: hours
    };
}

async function runOptimization() {
    const btn = document.getElementById('btn-optimize');
    const btnText = document.getElementById('btn-optimize-text');
    const spinner = document.getElementById('btn-optimize-spinner');
    const errorAlert = document.getElementById('error-alert');

    btn.disabled = true;
    btnText.textContent = 'Optimizing Schedule...';
    spinner.classList.remove('hidden');
    errorAlert.classList.add('hidden');

    try {
        const payload = buildRequestPayload();
        const res = await fetch('/optimize-energy', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });

        const data = await res.json();

        if (!res.ok) {
            showError(data.message || data.error || 'Optimization error occurred', data.details);
            return;
        }

        renderResults(data, payload);
    } catch (err) {
        showError('Network or server error while executing optimization.', [err.message]);
    } finally {
        btn.disabled = false;
        btnText.textContent = '⚡ Optimize Energy Schedule';
        spinner.classList.add('hidden');
    }
}

function showError(msg, details) {
    const errorAlert = document.getElementById('error-alert');
    const errorMsg = document.getElementById('error-message');
    const errorDetails = document.getElementById('error-details');

    errorMsg.textContent = msg;
    errorDetails.innerHTML = '';
    if (details && Array.isArray(details)) {
        details.forEach(d => {
            const li = document.createElement('li');
            li.textContent = d;
            errorDetails.appendChild(li);
        });
    }
    errorAlert.classList.remove('hidden');
}

function renderResults(res, req) {
    // 0. Update Official Benchmark Ground Truth Card if this matches one of the official hackathon cases
    const benchmarkCard = document.getElementById('benchmark-comparison-card');
    const matchedCase = allCases.find(c => c.id === res.scenario_id);
    if (matchedCase && matchedCase.expected_output && benchmarkCard) {
        benchmarkCard.classList.remove('hidden');
        const exp = matchedCase.expected_output;
        document.getElementById('bench-ref-cost').textContent = Math.round(exp.total_cost_bdt).toLocaleString('en-US') + ' BDT';
        document.getElementById('bench-opt-cost').textContent = Math.round(res.total_cost_bdt).toLocaleString('en-US') + ' BDT';
        
        document.getElementById('bench-ref-grid').textContent = exp.total_grid_kwh.toFixed(1) + ' kWh';
        document.getElementById('bench-opt-grid').textContent = res.total_grid_kwh.toFixed(1) + ' kWh';
        
        document.getElementById('bench-ref-peak').textContent = exp.peak_grid_kwh.toFixed(1) + ' kW';
        document.getElementById('bench-opt-peak').textContent = res.peak_grid_kwh.toFixed(1) + ' kW';
        
        document.getElementById('benchmark-rationale').textContent = matchedCase.rationale || '';

        const costDiff = Math.abs(res.total_cost_bdt - exp.total_cost_bdt);
        const badge = document.getElementById('benchmark-match-badge');
        if (costDiff <= 0.05) {
            badge.textContent = '✓ 100% EXACT MATCH';
            badge.style.background = 'rgba(16, 185, 129, 0.2)';
            badge.style.color = '#34d399';
            badge.style.borderColor = '#34d399';
        } else {
            const pct = Math.abs((res.total_cost_bdt - exp.total_cost_bdt) / exp.total_cost_bdt * 100).toFixed(2);
            badge.textContent = `Δ ${pct}% vs Reference`;
            badge.style.background = 'rgba(6, 182, 212, 0.2)';
            badge.style.color = '#38bdf8';
            badge.style.borderColor = '#38bdf8';
        }
    } else if (benchmarkCard) {
        benchmarkCard.classList.add('hidden');
    }

    // 1. Update KPI Cards
    document.getElementById('kpi-total-grid').textContent = res.total_grid_kwh.toLocaleString('en-US', { minimumFractionDigits: 1, maximumFractionDigits: 1 });
    document.getElementById('kpi-total-cost').textContent = Math.round(res.total_cost_bdt).toLocaleString('en-US');
    document.getElementById('kpi-peak-grid').textContent = res.peak_grid_kwh.toLocaleString('en-US', { minimumFractionDigits: 1, maximumFractionDigits: 1 });

    // Battery Neutrality Check
    const lastHour = res.hourly_plan && res.hourly_plan[23];
    const initialKwh = req.battery.initial_energy_kwh;
    const finalKwh = lastHour ? lastHour.battery_energy_after_kwh : 0;
    const isNeutral = Math.abs(finalKwh - initialKwh) < 1e-3;

    const neutralityElem = document.getElementById('kpi-neutrality');
    if (isNeutral) {
        neutralityElem.innerHTML = `<span style="color:#34d399">✓ Verified</span>`;
        document.getElementById('kpi-neutrality-sub').textContent = `End E₂₃ = ${finalKwh} kWh == Start ${initialKwh} kWh`;
    } else {
        neutralityElem.innerHTML = `<span style="color:#f43f5e">⚠ Off Target</span>`;
        document.getElementById('kpi-neutrality-sub').textContent = `End ${finalKwh} kWh ≠ Start ${initialKwh} kWh`;
    }

    // Plan Summary Text
    document.getElementById('plan-summary-text').textContent = res.plan_summary || 'Schedule optimized successfully.';

    // 2. Directives Interpretation
    renderDirectives(res.directive_interpretation || []);

    // 3. Render Charts
    renderCharts(res, req);

    // 4. Render Schedule Table
    renderScheduleTable(res.hourly_plan || []);

    // 5. Raw JSON Inspector
    document.getElementById('json-output').textContent = JSON.stringify(res, null, 2);
}

function renderDirectives(directives) {
    const container = document.getElementById('directives-list');
    container.innerHTML = '';

    if (directives.length === 0) {
        container.innerHTML = '<div style="color:var(--text-muted);font-size:0.85rem;">No operator notes provided.</div>';
        return;
    }

    directives.forEach(d => {
        const card = document.createElement('div');
        card.className = 'directive-card';

        const isApplies = d.applies;
        card.style.setProperty('--directive-color', isApplies ? '#10b981' : '#64748b');

        let adjustmentText = '';
        if (d.structured_adjustment) {
            const adj = d.structured_adjustment;
            const parts = [];
            if (adj.hours && adj.hours.length > 0) {
                parts.push(`Hours: [${adj.hours.join(', ')}]`);
            }
            if (adj.factor !== null && adj.factor !== undefined) {
                parts.push(`Factor: ${adj.factor}`);
            }
            if (adj.reserve_kwh !== null && adj.reserve_kwh !== undefined) {
                parts.push(`Reserve: ${adj.reserve_kwh} kWh`);
            }
            if (adj.max_grid_kwh !== null && adj.max_grid_kwh !== undefined) {
                parts.push(`Max Grid: ${adj.max_grid_kwh} kWh`);
            }
            adjustmentText = parts.join(' | ');
        }

        card.innerHTML = `
            <div class="directive-header">
                <span class="directive-type">${d.directive_type}</span>
                <span class="directive-pill ${isApplies ? 'pill-applied' : 'pill-noop'}">${isApplies ? 'APPLIED' : 'NO-OP'}</span>
            </div>
            <div class="directive-explanation">${d.explanation || ''}</div>
            ${adjustmentText ? `<div class="directive-params">${adjustmentText}</div>` : ''}
        `;
        container.appendChild(card);
    });
}

function renderCharts(res, req) {
    const hours = (res.hourly_plan || []).map(p => `Hour ${p.hour.toString().padStart(2, '0')}`);
    const gridImport = (res.hourly_plan || []).map(p => p.grid_kwh);
    const solarUsed = (res.hourly_plan || []).map(p => p.solar_used_kwh);
    const batteryDischarge = (res.hourly_plan || []).map(p => p.battery_action === 'discharge' ? p.battery_kwh : 0);
    const demand = (req.hours || []).map(h => h.demand_kwh);

    // Energy Dispatch Mix Chart
    const ctxEnergy = document.getElementById('chart-energy-mix').getContext('2d');
    if (energyChart) energyChart.destroy();

    energyChart = new Chart(ctxEnergy, {
        type: 'bar',
        data: {
            labels: hours,
            datasets: [
                {
                    label: 'Campus Demand (kWh)',
                    data: demand,
                    type: 'line',
                    borderColor: '#f43f5e',
                    backgroundColor: 'rgba(244, 63, 94, 0.1)',
                    borderWidth: 2,
                    tension: 0.3,
                    yAxisID: 'y'
                },
                {
                    label: 'Solar Used (kWh)',
                    data: solarUsed,
                    backgroundColor: 'rgba(245, 158, 11, 0.85)',
                    stack: 'supply'
                },
                {
                    label: 'Battery Discharge (kWh)',
                    data: batteryDischarge,
                    backgroundColor: 'rgba(16, 185, 129, 0.85)',
                    stack: 'supply'
                },
                {
                    label: 'Grid Import (kWh)',
                    data: gridImport,
                    backgroundColor: 'rgba(6, 182, 212, 0.85)',
                    stack: 'supply'
                }
            ]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            interaction: { mode: 'index', intersect: false },
            scales: {
                x: {
                    grid: { color: 'rgba(255, 255, 255, 0.05)' },
                    ticks: { color: '#94a3b8', maxRotation: 45, font: { size: 10 } }
                },
                y: {
                    stacked: true,
                    grid: { color: 'rgba(255, 255, 255, 0.05)' },
                    ticks: { color: '#94a3b8' },
                    title: { display: true, text: 'Energy (kWh)', color: '#64748b' }
                }
            },
            plugins: {
                legend: {
                    position: 'top',
                    labels: { color: '#f8fafc', font: { size: 11 }, boxWidth: 12 }
                }
            }
        }
    });

    // Battery State of Charge Chart
    const batteryEnergyAfter = (res.hourly_plan || []).map(p => p.battery_energy_after_kwh);
    const capacityLine = new Array(24).fill(req.battery.capacity_kwh);
    const minReserveLine = new Array(24).fill(req.battery.minimum_energy_kwh);

    const ctxBattery = document.getElementById('chart-battery-soc').getContext('2d');
    if (batteryChart) batteryChart.destroy();

    batteryChart = new Chart(ctxBattery, {
        type: 'line',
        data: {
            labels: hours,
            datasets: [
                {
                    label: 'Battery Storage (kWh)',
                    data: batteryEnergyAfter,
                    borderColor: '#10b981',
                    backgroundColor: 'rgba(16, 185, 129, 0.15)',
                    fill: true,
                    borderWidth: 3,
                    tension: 0.35,
                    pointBackgroundColor: '#10b981',
                    pointRadius: 3
                },
                {
                    label: 'Total Capacity',
                    data: capacityLine,
                    borderColor: 'rgba(148, 163, 184, 0.4)',
                    borderDash: [5, 5],
                    borderWidth: 1.5,
                    pointRadius: 0,
                    fill: false
                },
                {
                    label: 'Minimum Reserve',
                    data: minReserveLine,
                    borderColor: 'rgba(244, 63, 94, 0.5)',
                    borderDash: [4, 4],
                    borderWidth: 1.5,
                    pointRadius: 0,
                    fill: false
                }
            ]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            scales: {
                x: {
                    grid: { color: 'rgba(255, 255, 255, 0.05)' },
                    ticks: { color: '#94a3b8', font: { size: 10 } }
                },
                y: {
                    min: 0,
                    grid: { color: 'rgba(255, 255, 255, 0.05)' },
                    ticks: { color: '#94a3b8' },
                    title: { display: true, text: 'Stored Energy (kWh)', color: '#64748b' }
                }
            },
            plugins: {
                legend: {
                    position: 'top',
                    labels: { color: '#f8fafc', font: { size: 11 }, boxWidth: 12 }
                }
            }
        }
    });
}

function renderScheduleTable(plan) {
    const tbody = document.getElementById('schedule-tbody');
    tbody.innerHTML = '';

    plan.forEach(row => {
        const tr = document.createElement('tr');
        const actionBadgeClass = row.battery_action === 'charge' ? 'action-charge' : (row.battery_action === 'discharge' ? 'action-discharge' : 'action-idle');

        tr.innerHTML = `
            <td><strong>${row.hour.toString().padStart(2, '0')}:00</strong></td>
            <td>${row.grid_kwh.toFixed(1)}</td>
            <td>${row.solar_used_kwh.toFixed(1)}</td>
            <td><span class="badge-action ${actionBadgeClass}">${row.battery_action}</span></td>
            <td>${row.battery_kwh.toFixed(1)}</td>
            <td><strong>${row.battery_energy_after_kwh.toFixed(1)}</strong></td>
        `;
        tbody.appendChild(tr);
    });
}
