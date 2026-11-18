using Gebeta.Core.Domain.CostCenters;

namespace Gebeta.Core.Tests;

public class CostCenterTests
{
    [Fact]
    public void Codes_are_short_upper_case_and_the_main_cost_centre_stays_active()
    {
        var estate = CostCenter.Create(Guid.NewGuid(), " estate ", "Cane estate").Value;
        Assert.Equal(("ESTATE", "Cane estate", false, true), (estate.Code, estate.Name, estate.IsMain, estate.IsActive));
        Assert.True(estate.Update("Cane estate", isActive: false).IsSuccess);

        Assert.True(CostCenter.Create(Guid.NewGuid(), "HEAD OFFICE", "Head office").IsFailure); // no spaces
        Assert.True(CostCenter.Create(Guid.NewGuid(), new string('X', 21), "Too long").IsFailure);
        Assert.True(CostCenter.Create(Guid.NewGuid(), "WS", " ").IsFailure);

        var main = CostCenter.Main(Guid.NewGuid());
        Assert.Equal(("MAIN", true), (main.Code, main.IsMain));
        Assert.True(main.Update("Main", isActive: false).IsFailure);
        Assert.True(main.Update("Head office", isActive: true).IsSuccess);
    }
}
