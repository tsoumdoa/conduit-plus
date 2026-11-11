using ConduitPlus.Core;
using Xunit;
namespace ConduitPlus.Core.Tests;
public class ShownCardTests
{
    [Fact] public void CentresInGap() => Assert.Equal(new Rect2(350, 200, 100, 100), ShownCard.Place(new(100,100),new(1000,500),12,200,600));
    [Fact] public void EmptySidesUseViewport() => Assert.Equal(new Rect2(450,200,100,100),ShownCard.Place(new(100,100),new(1000,500),12,0,1000));
    [Fact] public void ClampsAtMargin() => Assert.Equal(12,ShownCard.Place(new(300,100),new(1000,500),12,0,100).X);
    [Fact] public void ClampsAtRightMargin() => Assert.Equal(688,ShownCard.Place(new(300,100),new(1000,500),12,990,1000).X);
    [Fact] public void TallCardStartsAtTop() => Assert.Equal(12,ShownCard.Place(new(100,900),new(1000,500),12,0,1000).Y);
    [Fact] public void FullFormFits() => Assert.Equal(CardForm.None,ShownCard.Form(new(){Id="m",Kind=CardKind.Metric},Theme.Paper,100,TextMetrics.Estimate));
    [Theory] [InlineData(CardKind.Table)] [InlineData(CardKind.Bar)]
    public void LargestRowsThatFit(CardKind kind)
    {
        var card = new CardSpec {Id="rows",Kind=kind,MaxRows=50,Rows=Enumerable.Range(0,50).Select(i=>new TableRow(i.ToString(),["x"])).ToArray(),Values=Enumerable.Repeat<double?>(1,50).ToArray()};
        var form=ShownCard.Form(card,Theme.Paper,200,TextMetrics.Estimate);
        Assert.True(form.VisibleRows >= 3);
        Assert.True(FormMeasure.Size(card,Theme.Paper,form,TextMetrics.Estimate).Height<=200);
        Assert.True(FormMeasure.Size(card,Theme.Paper,new(form.VisibleRows+1),TextMetrics.Estimate).Height>200);
    }
    [Theory] [InlineData(CardKind.Line,150)] [InlineData(CardKind.Metric,60)] [InlineData(CardKind.Text,110)]
    public void CompactFits(CardKind kind,double height)
    {
        var card=new CardSpec {Id="compact",Kind=kind,Text=string.Join(" ",Enumerable.Repeat("Long text",100))};
        Assert.True(ShownCard.Form(card,Theme.Paper,height,TextMetrics.Estimate).Compact);
    }
    [Fact] public void ImpossibleFitReturnsNone() => Assert.Equal(CardForm.None,ShownCard.Form(new(){Id="m",Kind=CardKind.Metric},Theme.Paper,1,TextMetrics.Estimate));
}
