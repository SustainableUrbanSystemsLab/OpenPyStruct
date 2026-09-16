using NUnit.Framework;
using OpenPyStruct.Core.Contract;
using OpenPyStruct.Core.Engine;
using OpenPyStruct.Core.Results;

namespace OpenPyStruct.Core.Tests;

public class TestEngine
{
    [Test]
    public void RunArgsForDocker()
    {
        var s = new EngineSettings { Image = "openpystruct:cuda", Cpus = 4, Gpu = true, Cli = "/usr/bin/docker" };
        var args = ContainerRunner.RunArgs(s, "/home/me/run1");
        Assert.That(args, Is.EqualTo(new[]
        {
            "run", "--rm", "--cpus", "4", "--gpus", "all", "-v", "/home/me/run1:/work",
            "openpystruct:cuda", "run", "/work/case.json", "/work/result.json",
        }));
    }

    [Test]
    public void RunArgsForPodmanWithPlatform()
    {
        var s = new EngineSettings { Gpu = true, Cli = "/opt/podman/bin/podman", Platform = "linux/amd64", ExtraArgs = { "--memory", "8g" } };
        var args = ContainerRunner.RunArgs(s, @"C:\Users\me\OpenPyStruct\runs\a");
        Assert.That(args, Does.Contain("--device").And.Contain("nvidia.com/gpu=all"));
        Assert.That(args, Does.Contain("--platform"));
        var list = args.ToList();
        Assert.That(list.IndexOf("--memory"), Is.LessThan(list.IndexOf("openpystruct")));
        Assert.That(args, Does.Contain(@"C:\Users\me\OpenPyStruct\runs\a:/work"));
    }

    [Test]
    public void ExposeFileMountsForeignFoldersOnce()
    {
        var s = new EngineSettings();
        var run = Path.Combine(Path.GetTempPath(), "ops-run");
        var inRun = ContainerRunner.ExposeFile(s, run, Path.Combine(run, "model.pt"));
        Assert.That(inRun, Is.EqualTo("/work/model.pt"));
        Assert.That(s.ExtraMounts, Is.Empty);
        var elsewhere = Path.Combine(Path.GetTempPath(), "models");
        var a = ContainerRunner.ExposeFile(s, run, Path.Combine(elsewhere, "a.pt"));
        var b = ContainerRunner.ExposeFile(s, run, Path.Combine(elsewhere, "b.json"));
        Assert.That(a, Is.EqualTo("/input/a.pt"));
        Assert.That(b, Is.EqualTo("/input/b.json"));
        Assert.That(s.ExtraMounts.Count, Is.EqualTo(1));
        Assert.That(s.ExtraMounts[0].ReadOnly, Is.True);
        var args = ContainerRunner.RunArgs(s, run);
        Assert.That(args, Does.Contain(Path.GetFullPath(elsewhere) + ":/input:ro"));
    }

    [Test]
    public void NativeModeUsesHostPathsAndTheInterpreter()
    {
        var s = new EngineSettings { Mode = EngineMode.Native, Device = "mps" };
        var run = Path.Combine(Path.GetTempPath(), "ops-native");
        Assert.That(EngineRunner.ExposeFile(s, run, Path.Combine("models", "a.pt")),
            Is.EqualTo(Path.GetFullPath(Path.Combine("models", "a.pt"))));
        Assert.That(s.ExtraMounts, Is.Empty);
        var args = NativeRunner.RunArgs(run);
        Assert.That(args, Is.EqualTo(new[] { "-m", "openpystruct.cli", "run",
            Path.Combine(run, "case.json"), Path.Combine(run, "result.json") }));
        Assert.That(EngineRunner.ToHostPath(s, run, "/some/host/dataset.json"), Is.EqualTo("/some/host/dataset.json"));
        var c = new EngineSettings();
        Assert.That(EngineRunner.ToHostPath(c, run, "/work/dataset.json"), Is.EqualTo(Path.Combine(run, "dataset.json")));
    }

    [Test]
    public void PrepareRunFolderStampsTheDevice()
    {
        var root = Path.Combine(Path.GetTempPath(), "ops-tests", Guid.NewGuid().ToString("N"));
        var s = new EngineSettings { RunsRoot = root, Mode = EngineMode.Native, Device = "mps" };
        var folder = EngineRunner.PrepareRunFolder(s, new CaseDocument { Task = "train" });
        var written = CaseDocument.FromJson(File.ReadAllText(Path.Combine(folder, "case.json")));
        Assert.That(written.Device, Is.EqualTo("mps"));
        var auto = EngineRunner.PrepareRunFolder(new EngineSettings { RunsRoot = root }, new CaseDocument());
        Assert.That(CaseDocument.FromJson(File.ReadAllText(Path.Combine(auto, "case.json"))).Device, Is.Null);
        Directory.Delete(root, true);
    }

    [Test]
    public void NativeInstallStepsBuildAVenvThenTorchThenThePackage()
    {
        var steps = NativeRunner.InstallSteps("/usr/bin/python3", "/tmp/venv", "/repo");
        Assert.That(steps.Count, Is.EqualTo(4));
        Assert.That(steps[0].Exe, Is.EqualTo("/usr/bin/python3"));
        Assert.That(steps[0].Args, Does.Contain("venv").And.Contain("/tmp/venv"));
        Assert.That(steps[2].Args, Does.Contain("torch"));
        Assert.That(steps[3].Args, Does.Contain("-e").And.Contain("/repo"));
        Assert.That(NativeRunner.ResolvePython(new EngineSettings { Python = "/nope/python" }), Is.Null);
    }

    [Test]
    public void BuildArgs()
    {
        var s = new EngineSettings { Image = "openpystruct" };
        Assert.That(ContainerRunner.BuildArgs(s, "/repo", "/repo/docker/Dockerfile"),
            Is.EqualTo(new[] { "build", "-t", "openpystruct", "-f", "/repo/docker/Dockerfile", "/repo" }));
    }

    [Test]
    public void RenderQuotesSpaces()
    {
        var line = ContainerRunner.Render("/opt/podman/bin/podman", new[] { "run", "-v", "/Users/me/My Runs/a:/work" });
        Assert.That(line, Is.EqualTo("/opt/podman/bin/podman run -v \"/Users/me/My Runs/a:/work\""));
    }

    [Test]
    public void ProgressLineParses()
    {
        Assert.That(ProgressLine.TryParse("PROGRESS 5 20 12.5", out var p), Is.True);
        Assert.That(p.Done, Is.EqualTo(5));
        Assert.That(p.Fraction, Is.EqualTo(0.25));
        Assert.That(p.Value, Is.EqualTo(12.5));
        Assert.That(ProgressLine.TryParse("PROGRESS 3 3 nan", out p), Is.True);
        Assert.That(double.IsNaN(p.Value));
        Assert.That(ProgressLine.TryParse("DONE 1.2s", out _), Is.False);
        Assert.That(ProgressLine.TryParse(null, out _), Is.False);
    }

    [Test]
    public void PrepareRunFolderWritesCaseAndClearsStaleResult()
    {
        var root = Path.Combine(Path.GetTempPath(), "ops-tests", Guid.NewGuid().ToString("N"));
        var s = new EngineSettings { RunsRoot = root, FeBackend = "numpy" };
        var doc = new CaseDocument { Task = "train" };
        var folder = ContainerRunner.PrepareRunFolder(s, doc, label: "my run/1");
        File.WriteAllText(Path.Combine(folder, "result.json"), "{}");
        var again = ContainerRunner.PrepareRunFolder(s, doc, folder: folder);
        Assert.That(again, Is.EqualTo(folder));
        Assert.That(File.Exists(Path.Combine(folder, "result.json")), Is.False);
        var written = CaseDocument.FromJson(File.ReadAllText(Path.Combine(folder, "case.json")));
        Assert.That(written.FeBackend, Is.EqualTo("numpy"));
        Assert.That(Path.GetFileName(folder), Does.EndWith("-my_run_1"));
        Directory.Delete(root, true);
    }

    [Test]
    public void ContainerCliNameAndMessages()
    {
        Assert.That(ContainerCli.Name(@"C:\Program Files\RedHat\Podman\podman.exe"), Is.EqualTo("podman"));
        Assert.That(ContainerCli.IsPodman("/opt/podman/bin/podman"), Is.True);
        Assert.That(ContainerCli.IsPodman("/usr/local/bin/docker"), Is.False);
        Assert.That(ContainerCli.DaemonDownMessage("/usr/local/bin/docker"), Does.Contain("Docker Desktop"));
        Assert.That(ContainerCli.Resolve("/definitely/not/here/podman"), Is.Null);
    }

    [Test]
    public void SectionDepthInvertsRectangle()
    {
        var h = SectionShape.DepthForInertia(0.3 * Math.Pow(0.6, 3) / 12, 0.3);
        Assert.That(h, Is.EqualTo(0.6).Within(1e-12));
        Assert.That(SectionShape.DepthForInertia(0, 0.3), Is.EqualTo(0));
        Assert.That(SectionShape.Normalize(5, 0, 10), Is.EqualTo(0.5));
        Assert.That(SectionShape.Normalize(50, 0, 10), Is.EqualTo(1));
    }

    [Test]
    public void DiagramsPairEnds()
    {
        var c = new CaseResult("LC", new[] { new[] { 0.0, 0, 0 }, new[] { 0.0, -0.02, 0 } }, new[] { 1.0 },
            new[] { 2.0 }, new[] { -2.0 }, new[] { 3.0 }, new[] { -4.0 });
        Assert.That(Diagrams.EndValues(c, Quantity.Moment)[0], Is.EqualTo(new[] { 3.0, -4.0 }));
        Assert.That(Diagrams.AbsMax(c, Quantity.Moment), Is.EqualTo(4));
        Assert.That(Diagrams.MaxDeflection(c), Is.EqualTo(0.02));
    }
}
