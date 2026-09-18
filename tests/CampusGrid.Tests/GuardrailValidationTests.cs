using CampusGrid.Models.Domain;
using CampusGrid.Models.Dto;
using CampusGrid.Validation;
using Xunit;

namespace CampusGrid.Tests;

public class GuardrailValidationTests
{
    private readonly DirectiveValidator _validator = new();

    [Fact]
    public void Validate_ValidNoOp_Passes()
    {
        var directive = new DirectiveInterpretationDto
        {
            NoteIndex = 0,
            Applies = false,
            DirectiveType = DirectiveType.NoOp,
            StructuredAdjustment = null,
            Explanation = "Unrelated note."
        };

        var exception = Record.Exception(() => _validator.Validate(directive));
        Assert.Null(exception);
    }

    [Fact]
    public void Validate_NoOpWithAppliesTrue_ThrowsDirectiveValidationException()
    {
        var directive = new DirectiveInterpretationDto
        {
            NoteIndex = 0,
            Applies = true, // invalid for no_op
            DirectiveType = DirectiveType.NoOp,
            StructuredAdjustment = null
        };

        Assert.Throws<DirectiveValidationException>(() => _validator.Validate(directive));
    }

    [Fact]
    public void Validate_NoOpWithNonNullAdjustment_ThrowsDirectiveValidationException()
    {
        var directive = new DirectiveInterpretationDto
        {
            NoteIndex = 0,
            Applies = false,
            DirectiveType = DirectiveType.NoOp,
            StructuredAdjustment = new StructuredAdjustment { Hours = new List<int> { 1, 2 } } // invalid for no_op
        };

        Assert.Throws<DirectiveValidationException>(() => _validator.Validate(directive));
    }

    [Fact]
    public void Validate_UnknownDirectiveType_ThrowsDirectiveValidationException()
    {
        var directive = new DirectiveInterpretationDto
        {
            NoteIndex = 0,
            Applies = true,
            DirectiveType = "invented_alien_directive",
            StructuredAdjustment = new StructuredAdjustment { Hours = new List<int> { 1, 2 } }
        };

        Assert.Throws<DirectiveValidationException>(() => _validator.Validate(directive));
    }

    [Fact]
    public void Validate_HoursOutOfRange_ThrowsDirectiveValidationException()
    {
        var directive = new DirectiveInterpretationDto
        {
            NoteIndex = 0,
            Applies = true,
            DirectiveType = DirectiveType.NoChargeWindow,
            StructuredAdjustment = new StructuredAdjustment { Hours = new List<int> { 10, 24 } } // 24 is out of range (0-23)
        };

        Assert.Throws<DirectiveValidationException>(() => _validator.Validate(directive));
    }

    [Fact]
    public void Validate_HoursNonAscending_ThrowsDirectiveValidationException()
    {
        var directive = new DirectiveInterpretationDto
        {
            NoteIndex = 0,
            Applies = true,
            DirectiveType = DirectiveType.NoChargeWindow,
            StructuredAdjustment = new StructuredAdjustment { Hours = new List<int> { 14, 13 } } // non-ascending
        };

        Assert.Throws<DirectiveValidationException>(() => _validator.Validate(directive));
    }

    [Fact]
    public void Validate_HoursDuplicate_ThrowsDirectiveValidationException()
    {
        var directive = new DirectiveInterpretationDto
        {
            NoteIndex = 0,
            Applies = true,
            DirectiveType = DirectiveType.NoChargeWindow,
            StructuredAdjustment = new StructuredAdjustment { Hours = new List<int> { 12, 12 } } // duplicate
        };

        Assert.Throws<DirectiveValidationException>(() => _validator.Validate(directive));
    }

    [Fact]
    public void Validate_SolarReductionInvalidFactor_ThrowsDirectiveValidationException()
    {
        var directive = new DirectiveInterpretationDto
        {
            NoteIndex = 0,
            Applies = true,
            DirectiveType = DirectiveType.SolarReduction,
            StructuredAdjustment = new StructuredAdjustment
            {
                Hours = new List<int> { 11, 12 },
                Factor = 1.5 // invalid factor > 1.0
            }
        };

        Assert.Throws<DirectiveValidationException>(() => _validator.Validate(directive));
    }

    [Fact]
    public void Validate_SolarReductionMissingFactor_ThrowsDirectiveValidationException()
    {
        var directive = new DirectiveInterpretationDto
        {
            NoteIndex = 0,
            Applies = true,
            DirectiveType = DirectiveType.SolarReduction,
            StructuredAdjustment = new StructuredAdjustment
            {
                Hours = new List<int> { 11, 12 },
                Factor = null
            }
        };

        Assert.Throws<DirectiveValidationException>(() => _validator.Validate(directive));
    }

    [Fact]
    public void Validate_InventedParameterOnNoChargeWindow_ThrowsDirectiveValidationException()
    {
        var directive = new DirectiveInterpretationDto
        {
            NoteIndex = 0,
            Applies = true,
            DirectiveType = DirectiveType.NoChargeWindow,
            StructuredAdjustment = new StructuredAdjustment
            {
                Hours = new List<int> { 11, 12 },
                Factor = 0.5 // invented parameter!
            }
        };

        Assert.Throws<DirectiveValidationException>(() => _validator.Validate(directive));
    }

    [Fact]
    public void Validate_BatteryReserveExceedsCapacity_ThrowsDirectiveValidationException()
    {
        var battery = new Battery { CapacityKwh = 200, InitialEnergyKwh = 100, MinimumEnergyKwh = 30 };
        var directive = new DirectiveInterpretationDto
        {
            NoteIndex = 0,
            Applies = true,
            DirectiveType = DirectiveType.MinimumBatteryReserve,
            StructuredAdjustment = new StructuredAdjustment
            {
                Hours = new List<int> { 18, 19 },
                MinimumEnergyKwh = 250 // exceeds capacity 200!
            }
        };

        Assert.Throws<DirectiveValidationException>(() => _validator.Validate(directive, battery));
    }
}
