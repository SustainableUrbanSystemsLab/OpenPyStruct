using NUnit.Framework;
using OpenPyStruct.Core.Contract;
using OpenPyStruct.Core.Geometry;

namespace OpenPyStruct.Core.Tests;

public class TestBeamBuilder
{
    [Test]
    public void SnapsSupportsToNodesAndOrdersThem()
    {
        var r = BeamBuilder.Build(20.0, 10, pins: new[] { 0.0 }, rollers: new[] { 8.9, 20.0 }, fixedSupports: Array.Empty<double>());
        Assert.That(r.Model.NodeCount, Is.EqualTo(11));
        Assert.That(r.Model.ElementCount, Is.EqualTo(10));
        Assert.That(r.Model.Supports.Select(s => s.Node), Is.EqualTo(new[] { 0, 4, 10 }));
        Assert.That(r.Model.Supports[1].Type, Is.EqualTo("roller"));
        Assert.That(r.MaxSnapError, Is.EqualTo(0.9).Within(1e-9));
        Assert.That(r.Model.Kind, Is.EqualTo("beam"));
        Assert.That(BeamBuilder.StabilityProblem(r.Model), Is.Null);
    }

    [Test]
    public void PinAndRollerOnOneNodeIsAPin()
    {
        var r = BeamBuilder.Build(10.0, 5, pins: new[] { 0.0 }, rollers: new[] { 0.1, 10.0 }, fixedSupports: Array.Empty<double>());
        Assert.That(r.Model.Supports.Count, Is.EqualTo(2));
        Assert.That(r.Model.Supports[0].Type, Is.EqualTo("pin"));
    }

    [Test]
    public void FlagsMechanisms()
    {
        var onlyRollers = BeamBuilder.Build(10.0, 5, Array.Empty<double>(), new[] { 0.0, 10.0 }, Array.Empty<double>()).Model;
        Assert.That(BeamBuilder.StabilityProblem(onlyRollers), Does.Contain("axially"));
        var onePin = BeamBuilder.Build(10.0, 5, new[] { 0.0 }, Array.Empty<double>(), Array.Empty<double>()).Model;
        Assert.That(BeamBuilder.StabilityProblem(onePin), Does.Contain("rotates"));
        var cantilever = BeamBuilder.Build(10.0, 5, Array.Empty<double>(), Array.Empty<double>(), new[] { 0.0 }).Model;
        Assert.That(BeamBuilder.StabilityProblem(cantilever), Is.Null);
    }

    [Test]
    public void RejectsBadArguments()
    {
        Assert.Throws<ArgumentException>(() => BeamBuilder.Build(0, 5, Array.Empty<double>(), Array.Empty<double>(), Array.Empty<double>()));
        Assert.Throws<ArgumentException>(() => BeamBuilder.Build(5, 0, Array.Empty<double>(), Array.Empty<double>(), Array.Empty<double>()));
    }
}

public class TestFrameBuilder
{
    private static FrameBuilder.Segment Seg(double x1, double y1, double z1, double x2, double y2, double z2) =>
        new(new Vec3(x1, y1, z1), new Vec3(x2, y2, z2));

    [Test]
    public void PortalFrameInXZPlane()
    {
        var segs = new[]
        {
            Seg(0, 0, 0, 0, 0, 3),      // left column
            Seg(0, 0, 3, 6, 0, 3),      // beam
            Seg(6, 0, 3, 6, 0, 0),      // right column, drawn top-down
        };
        var r = FrameBuilder.Build(segs, Array.Empty<Vec3>(), Array.Empty<Vec3>(), Array.Empty<Vec3>(), 1e-3);
        Assert.That(r.Model.NodeCount, Is.EqualTo(4));
        Assert.That(r.Model.ElementCount, Is.EqualTo(3));
        Assert.That(r.Kinds, Is.EqualTo(new[] { FrameBuilder.MemberKind.Column, FrameBuilder.MemberKind.Beam, FrameBuilder.MemberKind.Column }));
        // auto-fixed base
        Assert.That(r.Model.Supports.Select(s => s.Type), Is.All.EqualTo("fixed"));
        Assert.That(r.Model.Supports.Count, Is.EqualTo(2));
        Assert.That(r.Warnings, Has.Some.Contains("lowest level"));
        // engine coordinates: x along the frame from 0, y up from 0
        var top = r.Model.Nodes[1];
        Assert.That(top[0], Is.EqualTo(0).Within(1e-9));
        Assert.That(top[1], Is.EqualTo(3).Within(1e-9));
        Assert.That(r.Model.Nodes.Max(n => n[0]), Is.EqualTo(6).Within(1e-9));
        Assert.That(r.MaxOutOfPlane, Is.EqualTo(0).Within(1e-9));
    }

    [Test]
    public void FrameDrawnAlongYAxisIsMappedAndRoundTrips()
    {
        var segs = new[] { Seg(2, 1, 0, 2, 1, 4), Seg(2, 1, 4, 2, 9, 4), Seg(2, 9, 4, 2, 9, 0) };
        var r = FrameBuilder.Build(segs, new[] { new Vec3(2, 1, 0), new Vec3(2, 9, 0) }, Array.Empty<Vec3>(),
            Array.Empty<Vec3>(), 1e-3);
        Assert.That(r.Plane.U.Y, Is.EqualTo(1).Within(1e-9));
        Assert.That(r.Model.Supports.Select(s => s.Node), Is.EqualTo(new[] { 0, 3 }));
        for (var i = 0; i < r.Model.NodeCount; i++)
        {
            var back = r.Plane.ToWorld(new Vec2(r.Model.Nodes[i][0], r.Model.Nodes[i][1]));
            Assert.That(back.DistanceTo(r.NodesWorld[i]), Is.LessThan(1e-9));
        }
        Assert.That(r.Warnings, Is.Empty);
    }

    [Test]
    public void PinnedAndRollerPointsSnapWithWarningWhenFar()
    {
        var segs = new[] { Seg(0, 0, 0, 0, 0, 3), Seg(0, 0, 3, 4, 0, 3), Seg(4, 0, 3, 4, 0, 0) };
        var r = FrameBuilder.Build(segs, Array.Empty<Vec3>(), new[] { new Vec3(0, 0, 0.5) }, new[] { new Vec3(4, 0, 0) }, 1e-2);
        Assert.That(r.Model.Supports.Select(s => (s.Node, s.Type)), Is.EqualTo(new[] { (0, "pin"), (3, "roller") }));
        Assert.That(r.Warnings, Has.Some.Contains("snapped"));
    }

    [Test]
    public void DuplicateAndDegenerateMembersAreDropped()
    {
        var segs = new[] { Seg(0, 0, 0, 0, 0, 3), Seg(0, 0, 3, 0, 0, 0), Seg(0, 0, 3, 0, 0, 3.0005) };
        var r = FrameBuilder.Build(segs, Array.Empty<Vec3>(), Array.Empty<Vec3>(), Array.Empty<Vec3>(), 1e-3);
        Assert.That(r.Model.ElementCount, Is.EqualTo(1));
        Assert.That(r.Warnings.Count(w => w.Contains("duplicate")), Is.EqualTo(1));
        Assert.That(r.Warnings.Count(w => w.Contains("shorter")), Is.EqualTo(1));
    }

    [Test]
    public void OutOfPlaneGeometryIsProjectedWithWarning()
    {
        var segs = new[] { Seg(0, 0, 0, 0, 0.2, 3), Seg(0, 0.2, 3, 6, -0.2, 3), Seg(6, -0.2, 3, 6, 0, 0) };
        var r = FrameBuilder.Build(segs, Array.Empty<Vec3>(), Array.Empty<Vec3>(), Array.Empty<Vec3>(), 1e-3);
        Assert.That(r.MaxOutOfPlane, Is.GreaterThan(0.1));
        Assert.That(r.Warnings, Has.Some.Contains("out of the analysis plane"));
    }

    [Test]
    public void ClassifiesBraces()
    {
        Assert.That(FrameBuilder.Classify(new Vec2(0, 0), new Vec2(3, 3)), Is.EqualTo(FrameBuilder.MemberKind.Brace));
        Assert.That(FrameBuilder.Classify(new Vec2(0, 0), new Vec2(0.1, 3)), Is.EqualTo(FrameBuilder.MemberKind.Column));
        Assert.That(FrameBuilder.Classify(new Vec2(0, 3), new Vec2(5, 3.1)), Is.EqualTo(FrameBuilder.MemberKind.Beam));
    }
}

public class TestAnalysisPlane
{
    [Test]
    public void RejectsVerticalAxis()
    {
        Assert.Throws<ArgumentException>(() => new AnalysisPlane(Vec3.Zero, Vec3.UnitZ));
    }

    [Test]
    public void SingleColumnFallsBackToWorldX()
    {
        var plane = AnalysisPlane.Fit(new[] { new Vec3(1, 1, 0), new Vec3(1, 1, 5) });
        Assert.That(plane.U, Is.EqualTo(Vec3.UnitX));
        Assert.That(plane.ToPlane(new Vec3(1, 1, 5)), Is.EqualTo(new Vec2(0, 5)));
    }
}
