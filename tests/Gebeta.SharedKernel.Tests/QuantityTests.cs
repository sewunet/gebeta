using Gebeta.SharedKernel;

namespace Gebeta.SharedKernel.Tests;

public class QuantityTests
{
    [Fact]
    public void Adds_quantities_of_the_same_unit()
    {
        Assert.Equal(new Quantity(5.5m, "Kg"), new Quantity(2m, "Kg") + new Quantity(3.5m, "kg"));
    }

    [Fact]
    public void Refuses_to_mix_units()
    {
        var ex = Assert.Throws<DomainException>(() => new Quantity(1m, "Kg") + new Quantity(1m, "Nos"));

        Assert.Equal("quantity.uom_mismatch", ex.Code);
    }

    [Fact]
    public void Converts_with_a_factor()
    {
        Assert.Equal(new Quantity(24m, "Nos"), new Quantity(2m, "Box").ConvertTo("Nos", 12m));
    }

    [Fact]
    public void Rejects_a_non_positive_conversion_factor()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Quantity(2m, "Box").ConvertTo("Nos", 0m));
    }
}
