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

/// <summary>
/// The native path end to end: Generate Data then Train through the dispatcher with a host Python
/// that has the package installed (OPENPYSTRUCT_PYTHON, else the Build-created venv / python3).
/// On a Mac with a Metal GPU the training device comes back as mps — the reason native mode exists.
/// </summary>
[Explicit("needs a host Python with openpystruct installed")]
public class TestNativeSmoke
{
    [Test]
    public void TrainsNativelyOnTheBestDevice()
    {
        var settings = new EngineSettings
        {
            Mode = EngineMode.Native,
            Python = Environment.GetEnvironmentVariable("OPENPYSTRUCT_PYTHON"),
            RunsRoot = Environment.GetEnvironmentVariable("OPENPYSTRUCT_RUNS_ROOT"),
            Device = Environment.GetEnvironmentVariable("OPENPYSTRUCT_DEVICE") ?? "auto",
        };
        var problem = NativeRunner.Readiness(settings, out var python, out var info);
        Assert.That(problem, Is.Null, problem);
        TestContext.Out.WriteLine($"python: {python}  info: {info}");

        var gen = new CaseDocument { Task = "generate_data", Optimizer = new OptimizerSettings { Epochs = 3, Patience = 100 } };
        gen.TaskParams["num_samples"] = 12; gen.TaskParams["n_elements"] = 10; gen.TaskParams["length"] = 20.0;
        gen.TaskParams["roller_x"] = new System.Text.Json.Nodes.JsonArray(10.0, 20.0); gen.TaskParams["max_forces"] = 2;
        var genFolder = EngineRunner.PrepareRunFolder(settings, gen, label: "native-gen");
        var (o1, r1) = EngineRunner.RunCase(settings, genFolder, TestContext.Out.WriteLine, null, CancellationToken.None);
        Assert.That(o1.Succeeded && r1?.Ok == true, Is.True, o1.Log + r1?.Error);
        var dataset = EngineRunner.ToHostPath(settings, genFolder, r1!.DatasetPath)!;
        Assert.That(File.Exists(dataset), dataset);

        var train = new CaseDocument { Task = "train" };
        train.TaskParams["kind"] = "fnn"; train.TaskParams["n_cases"] = 3; train.TaskParams["epochs"] = 3;
        train.TaskParams["batch_size"] = 2; train.TaskParams["hidden_units"] = 16; train.TaskParams["num_blocks"] = 1;
        var trainFolder = EngineRunner.PrepareRunFolder(settings, train, label: "native-train");
        train.TaskParams["dataset"] = EngineRunner.ExposeFile(settings, trainFolder, dataset);
        File.WriteAllText(Path.Combine(trainFolder, ContainerRunner.CaseFileName), train.ToJson());
        var (o2, r2) = EngineRunner.RunCase(settings, trainFolder, TestContext.Out.WriteLine, null, CancellationToken.None);
        Assert.That(o2.Succeeded && r2?.Ok == true, Is.True, o2.Log + r2?.Error);
        TestContext.Out.WriteLine("trained on: " + r2!.Device);
        Assert.That(r2.Device, Is.Not.Null.And.Not.Empty);
        Assert.That(File.Exists(Path.Combine(trainFolder, "model.pt")));
        if (settings.Device != "auto") Assert.That(r2.Device, Is.EqualTo(settings.Device));
    }
}
