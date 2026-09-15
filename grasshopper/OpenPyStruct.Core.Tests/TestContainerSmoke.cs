using NUnit.Framework;
using OpenPyStruct.Core.Contract;
using OpenPyStruct.Core.Engine;
using OpenPyStruct.Core.Geometry;

namespace OpenPyStruct.Core.Tests;

/// <summary>
/// End-to-end through a real container: needs Podman or Docker running and the image built
/// (<c>docker/build.sh</c>). [Explicit] so the ordinary test run never depends on it:
/// <c>dotnet test --filter FullyQualifiedName~ContainerSmoke</c>.
/// Set OPENPYSTRUCT_IMAGE / OPENPYSTRUCT_PLATFORM / OPENPYSTRUCT_RUNS_ROOT to test another image,
/// platform or run folder root.
/// </summary>
[Explicit("needs a container engine and the openpystruct image")]
public class TestContainerSmoke
{
    [Test]
    public void OptimizesABeamInTheContainer()
    {
        var settings = new EngineSettings
        {
            Image = Environment.GetEnvironmentVariable("OPENPYSTRUCT_IMAGE") ?? EngineSettings.DefaultImage,
            Platform = Environment.GetEnvironmentVariable("OPENPYSTRUCT_PLATFORM"),
            RunsRoot = Environment.GetEnvironmentVariable("OPENPYSTRUCT_RUNS_ROOT"),
            TimeoutMinutes = 10,
        };
        var problem = ContainerRunner.Readiness(settings, out var cli);
        Assert.That(problem, Is.Null, problem);
        TestContext.Out.WriteLine("cli: " + cli);

        var beam = BeamBuilder.Build(20.0, 20, new[] { 0.0 }, new[] { 10.0, 20.0 }, Array.Empty<double>());
        var doc = new CaseDocument
        {
            Task = "optimize",
            Model = beam.Model,
            Optimizer = new OptimizerSettings { Epochs = 30, Patience = 100 },
            LoadCases = { new LoadCase { Name = "LC1", PointLoads = { new PointLoad { Node = 5, Fy = -1e5 } } } },
        };
        var folder = ContainerRunner.PrepareRunFolder(settings, doc, label: "smoke");
        var log = new List<string>();
        var progress = new List<ProgressLine>();
        var (outcome, result) = ContainerRunner.RunCase(settings, folder, log.Add, progress.Add, CancellationToken.None);
        TestContext.Out.WriteLine(outcome.Log);
        Assert.That(outcome.Succeeded, Is.True, outcome.Log);
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Ok, Is.True, result.Error);
        Assert.That(result.I!.Length, Is.EqualTo(20));
        Assert.That(progress.Last().Done, Is.EqualTo(30));
        Assert.That(result.Cases[0].MomentI.Length, Is.EqualTo(20));
    }
}
