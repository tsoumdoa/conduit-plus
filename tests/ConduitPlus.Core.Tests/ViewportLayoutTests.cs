using ConduitPlus.Core;
using Xunit;
namespace ConduitPlus.Core.Tests;
public class ViewportLayoutTests
{
    [Fact]
    public void Sweep()
    {
        foreach(var w in new[]{480,960,1920,3840}) foreach(var h in new[]{360,900,2160}) foreach(var dpi in new[]{1,1.25,1.5,2}) foreach(var size in new[]{.75,1,1.5}) foreach(var count in new[]{1,2,3})
        {
            var owners=Enumerable.Range(1,count).Select(i=>new LayoutParticipant(new Guid(i,0,0,new byte[8]),size,Theme.Paper,[..StructuralDemo.Create(),StructuralDemo.Line(),..StructuralDemo.Extras()])).ToArray();
            var r=new LayoutRequest(new(w,h),dpi,owners,TextMetrics.Estimate,new());
            var s=ViewportLayout.Solve(r);
            Assert.Equal(CardJson.Serialize(s),CardJson.Serialize(ViewportLayout.Solve(r)));
            Assert.Equal(owners.Sum(p=>p.Cards.Count),s.Placements.Count+s.Hidden.Count);
            foreach(var p in s.Placements)
            {
                Assert.True(p.X>=0 && p.Y>=0 && p.X+p.Width<=w+1e-6 && p.Y+p.Height<=h+1e-6);
                foreach(var q in s.Placements.Where(q=>q!=p)) Assert.False(p.X<q.X+q.Width && q.X<p.X+p.Width && p.Y<q.Y+q.Height && q.Y<p.Y+p.Height);
                if(s.Chip is {} c) Assert.False(p.X<c.X+c.Width && c.X<p.X+p.Width && p.Y<c.Y+c.Height && c.Y<p.Y+p.Height);
            }
        }
    }
    [Fact]
    public void FormsAndSignature()
    {
        foreach(var c in StructuralDemo.Create().Concat(StructuralDemo.Extras()).Append(StructuralDemo.Line())) Assert.Equal(CardMeasure.Measure(c,Theme.Paper),FormMeasure.Size(c,Theme.Paper,CardForm.None,TextMetrics.Measurer));
        var card=StructuralDemo.Line(); var id=Guid.NewGuid();
        LayoutRequest Request(CardSpec c)=>new(new(1920,1080),1,[new(id,1,Theme.Paper,[c])],TextMetrics.Estimate,new());
        Assert.Equal(ViewportLayout.Signature(Request(card)),ViewportLayout.Signature(Request(card with{Rev=55,HoverKey="a",ScrollOffset=2,SortColumn=1,SelectedKeys=["b"]})));
        Assert.NotEqual(ViewportLayout.Signature(Request(card)),ViewportLayout.Signature(Request(card with{Width=500})));
    }
}

