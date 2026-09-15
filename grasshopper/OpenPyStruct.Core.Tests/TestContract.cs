using System.Text.Json.Nodes;
using NUnit.Framework;
using OpenPyStruct.Core.Contract;

namespace OpenPyStruct.Core.Tests;

public class TestContract
{
    [Test]
    public void CaseRoundTripsThroughJson()
    {
        var doc = new CaseDocument
        {
            Task = "optimize",
            Model = new StructuralModel
            {
                Kind = "beam",
                Nodes = { new[] { 0.0, 0.0 }, new[] { 1.0, 0.0 } },
                Elements = { new[] { 0, 1 } },
                Supports = { new Support { Node = 0, Type = Support.Pin }, new Support { Node = 1, Type = Support.Roller } },
            },
            LoadCases = { new LoadCase { Name = "LC1", PointLoads = { new PointLoad { Node = 1, Fy = -5 } },
                ElementLoads = { new ElementLoad { Element = 0, Wy = -2 } } } },
        };
        doc.TaskParams["model"] = "model.pt";
        var json = doc.ToJson();
        Assert.That(json, Does.Contain("\"schema\": \"openpystruct.case/1\""));
        Assert.That(json, Does.Contain("\"alpha_moment\""));
        Assert.That(json, Does.Contain("\"I0\""));
        var back = CaseDocument.FromJson(json);
        Assert.That(back.Model!.Supports[1].Type, Is.EqualTo("roller"));
        Assert.That(back.LoadCases[0].ElementLoads[0].Wy, Is.EqualTo(-2));
        Assert.That(back.TaskParams["model"]!.GetValue<string>(), Is.EqualTo("model.pt"));
        Assert.That(back.FeBackend, Is.Null);
    }

    [Test]
    public void ResultParsesOptimizePayload()
    {
        const string json = """
        {"schema":"openpystruct.result/1","task":"optimize","ok":true,"error":null,"elapsed_s":1.5,
         "I":[0.1,0.2],"epochs":12,"stopped_early":true,"best_loss":3.5,
         "loss_history":{"total":[5,4],"primary":[1,1],"bending":[2,2],"shear":[2,1]},
         "cases":[{"name":"LC1","displacements":[[0,0,0],[0,-0.01,0.001],[0,0,0]],
                   "axial":[0,0],"shear_i":[1,2],"shear_j":[3,4],"moment_i":[5,6],"moment_j":[7,8]}]}
        """;
        var r = ResultDocument.Parse(json);
        Assert.That(r.Ok, Is.True);
        Assert.That(r.I, Is.EqualTo(new[] { 0.1, 0.2 }));
        Assert.That(r.Epochs, Is.EqualTo(12));
        Assert.That(r.BestLoss, Is.EqualTo(3.5));
        Assert.That(r.Losses!.Total, Is.EqualTo(new[] { 5.0, 4.0 }));
        var c = r.Cases[0];
        Assert.That(c.MomentJ, Is.EqualTo(new[] { 7.0, 8.0 }));
        Assert.That(c.Displacements[1][1], Is.EqualTo(-0.01));
        Assert.That(c.ElementCount, Is.EqualTo(2));
    }

    [Test]
    public void ResultCarriesErrors()
    {
        var r = ResultDocument.Parse("""{"schema":"openpystruct.result/1","task":"train","ok":false,"error":"boom"}""");
        Assert.That(r.Ok, Is.False);
        Assert.That(r.Error, Is.EqualTo("boom"));
        Assert.That(r.I, Is.Null);
        Assert.That(r.Cases, Is.Empty);
    }

    [Test]
    public void ResultRejectsForeignSchema()
    {
        Assert.Throws<InvalidDataException>(() => ResultDocument.Parse("""{"schema":"other/1"}"""));
    }
}
