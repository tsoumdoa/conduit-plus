using Xunit;
using ConduitPlus.Core;
public class OverlaySizeTests { [Theory] [InlineData(null,1,false)] [InlineData(0.5,0.75,true)] [InlineData(2.0,1.5,true)] [InlineData(double.NaN,1,true)] [InlineData(double.PositiveInfinity,1,true)] [InlineData(double.NegativeInfinity,1,true)] [InlineData(1.25,1.25,false)] public void Clamp(double? v,double expected,bool clamped) => Assert.Equal((expected,clamped),OverlaySize.Clamp(v)); }
