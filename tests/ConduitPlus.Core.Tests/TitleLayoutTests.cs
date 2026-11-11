using ConduitPlus.Core;
using Xunit;
namespace ConduitPlus.Core.Tests;
public class TitleLayoutTests
{
    [Fact]
    public void TitlesParticipateInInvariantSweep()
    {
        foreach(var w in new[]{120,480,600,1600}) foreach(var h in new[]{90,360,900,1200})
        {
            var cards = new[]{Slot.Tl,Slot.Tc,Slot.Tr,Slot.Bl,Slot.Bc,Slot.Br}.Select((slot,i)=>new CardSpec{Id=$"t{i}",Kind=CardKind.Title,Title="Heading",Subtitle="Subtitle",Text="Description",WiredSlot=slot}).Concat(StructuralDemo.Create()).ToArray();
            var r=new LayoutRequest(new(w,h),1,[new(Guid.Empty,1,Theme.Paper,cards)],TextMetrics.Estimate,new());
            var s=ViewportLayout.Solve(r);
            Assert.Equal(cards.Length,s.Placements.Count+s.Hidden.Count);
            Assert.Equal(cards.Length,s.Placements.Select(p=>p.CardId).Concat(s.Hidden.Select(p=>p.CardId)).Distinct().Count());
            Assert.Equal(CardJson.Serialize(s),CardJson.Serialize(ViewportLayout.Solve(r)));
            foreach(var p in s.Placements)
            {
                Assert.True(p.X>=0 && p.Y>=0 && p.X+p.Width<=w+1e-6 && p.Y+p.Height<=h+1e-6);
                foreach(var q in s.Placements.Where(q=>q!=p)) Assert.False(p.X<q.X+q.Width && q.X<p.X+p.Width && p.Y<q.Y+q.Height && q.Y<p.Y+p.Height);
            }
        }
    }
}
