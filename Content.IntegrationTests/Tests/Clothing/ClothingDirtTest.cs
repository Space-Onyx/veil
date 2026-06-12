using Content.Goobstation.Maths.FixedPoint;
using Content.Shared._Onyx.Clothing;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests.Clothing;

[TestFixture]
[TestOf(typeof(ClothingDirtSystem))]
public sealed class ClothingDirtTest
{
    [Test]
    public async Task WashingRemovesRoundedTraceAmounts()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var clothing = server.EntMan.SpawnEntity("ClothingUniformJumpsuitColorGrey", MapCoordinates.Nullspace);
            var dirtSystem = server.System<ClothingDirtSystem>();
            var solutionSystem = server.System<SharedSolutionContainerSystem>();
            var source = new Solution();
            source.AddReagent("Blood", FixedPoint2.New(2));
            source.AddReagent("Vomit", FixedPoint2.New(0.01f));

            Assert.That(dirtSystem.TryDirtyClothing(clothing, source, source.Volume), Is.True);
            Assert.That(
                dirtSystem.TryWashClothing(clothing, new ReagentId("Water", null), source.Volume),
                Is.True);
            Assert.That(
                solutionSystem.TryGetSolution(clothing, ClothingDirtSystem.DefaultSolutionName, out _, out var dirt),
                Is.True);
            Assert.That(dirt.Volume, Is.EqualTo(FixedPoint2.Zero));
        });

        await pair.CleanReturnAsync();
    }
}
