using VepMod.Enemies.Whispral;
using Xunit;

namespace Haunting.Tests;

public class AttachmentsTests
{
    private const int Whispral = 101;
    private const int OtherWhispral = 102;

    private readonly Attachments attachments = new();

    [Fact]
    public void FirstAttachMakesTheVictimHaunted()
    {
        Assert.Equal(HauntedTransition.BecameHaunted, attachments.Attach(Whispral));
        Assert.True(attachments.IsHaunted);
    }

    [Fact]
    public void SecondWhispralAttachingIsNoChange()
    {
        attachments.Attach(Whispral);

        Assert.Equal(HauntedTransition.NoChange, attachments.Attach(OtherWhispral));
        Assert.True(attachments.IsHaunted);
    }

    [Fact]
    public void FirstOfTwoDetachingIsNoChange()
    {
        attachments.Attach(Whispral);
        attachments.Attach(OtherWhispral);

        Assert.Equal(HauntedTransition.NoChange, attachments.Detach(Whispral));
        Assert.True(attachments.IsHaunted);
    }

    [Fact]
    public void LastDetachingStopsTheHaunting()
    {
        attachments.Attach(Whispral);
        attachments.Attach(OtherWhispral);
        attachments.Detach(Whispral);

        Assert.Equal(HauntedTransition.StoppedBeingHaunted, attachments.Detach(OtherWhispral));
        Assert.False(attachments.IsHaunted);
    }

    [Fact]
    public void ReattachingTheSameWhispralIsANoOp()
    {
        attachments.Attach(Whispral);

        Assert.Equal(HauntedTransition.NoChange, attachments.Attach(Whispral));
        Assert.Equal(HauntedTransition.StoppedBeingHaunted, attachments.Detach(Whispral));
    }

    [Fact]
    public void DetachingAWhispralThatNeverAttachedIsANoOp()
    {
        attachments.Attach(Whispral);

        Assert.Equal(HauntedTransition.NoChange, attachments.Detach(OtherWhispral));
        Assert.True(attachments.IsHaunted);
    }

    [Fact]
    public void DetachingTwiceIsANoOp()
    {
        attachments.Attach(Whispral);
        attachments.Detach(Whispral);

        Assert.Equal(HauntedTransition.NoChange, attachments.Detach(Whispral));
        Assert.False(attachments.IsHaunted);
    }

    [Fact]
    public void AttachAfterAHauntingEndedStartsANewOne()
    {
        attachments.Attach(Whispral);
        attachments.Detach(Whispral);

        Assert.Equal(HauntedTransition.BecameHaunted, attachments.Attach(Whispral));
        Assert.True(attachments.IsHaunted);
    }
}
