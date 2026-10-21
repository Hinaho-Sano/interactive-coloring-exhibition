using System;

public enum HandAction
{
    None,
    Follow,
    Pet,
    Tease,
    Rest
}

// Converts raw palm velocity into a small vocabulary shared by all animals.
// "Tease" is a quick left-right oscillation, so it works as a prop-free
// prototype of a cat teaser. A tracked physical prop can feed the same action.
public sealed class InteractionGestures
{
    sealed class Tracker
    {
        public HandAction Action;
        public float StillTime;
        public float FlipWindow;
        public float TeaseHold;
        public int HorizontalFlips;
        public int HorizontalSign;
    }

    readonly Tracker[] trackers = { new Tracker(), new Tracker() };

    public HandAction this[int hand]
        => hand >= 0 && hand < trackers.Length ? trackers[hand].Action : HandAction.None;

    public void TriggerTease(int hand, float seconds = .9f)
    {
        if (hand < 0 || hand >= trackers.Length) return;
        trackers[hand].TeaseHold = Math.Max(trackers[hand].TeaseHold, Math.Max(.1f, seconds));
        trackers[hand].Action = HandAction.Tease;
    }

    public void Reset()
    {
        foreach (var tracker in trackers)
        {
            tracker.Action = HandAction.None;
            tracker.StillTime = tracker.FlipWindow = tracker.TeaseHold = 0f;
            tracker.HorizontalFlips = tracker.HorizontalSign = 0;
        }
    }

    public void Step(float dt, FieldHand left, FieldHand right)
    {
        if (float.IsNaN(dt) || float.IsInfinity(dt) || dt <= 0f) return;
        dt = Math.Min(.05f, dt);
        StepOne(trackers[0], dt, left);
        StepOne(trackers[1], dt, right);
    }

    static void StepOne(Tracker tracker, float dt, FieldHand hand)
    {
        if (!hand.Active || !hand.Open)
        {
            tracker.Action = HandAction.None;
            tracker.StillTime = tracker.FlipWindow = tracker.TeaseHold = 0f;
            tracker.HorizontalFlips = tracker.HorizontalSign = 0;
            return;
        }

        float speed = (float)Math.Sqrt(hand.VX * hand.VX + hand.VY * hand.VY);
        if (speed < .055f) tracker.StillTime += dt;
        else tracker.StillTime = Math.Max(0f, tracker.StillTime - dt * 2f);

        bool horizontalStroke = Math.Abs(hand.VX) >= .32f && Math.Abs(hand.VX) > Math.Abs(hand.VY) * 1.15f;
        if (horizontalStroke)
        {
            int sign = hand.VX >= 0f ? 1 : -1;
            if (tracker.HorizontalSign != 0 && sign != tracker.HorizontalSign)
                tracker.HorizontalFlips++;
            tracker.HorizontalSign = sign;
            tracker.FlipWindow = .8f;
        }

        tracker.FlipWindow = Math.Max(0f, tracker.FlipWindow - dt);
        if (tracker.FlipWindow <= 0f)
        {
            tracker.HorizontalFlips = 0;
            tracker.HorizontalSign = 0;
        }

        if (tracker.HorizontalFlips >= 2)
        {
            tracker.TeaseHold = .6f;
            tracker.HorizontalFlips = 0;
        }
        tracker.TeaseHold = Math.Max(0f, tracker.TeaseHold - dt);

        if (tracker.TeaseHold > 0f) tracker.Action = HandAction.Tease;
        else if (tracker.StillTime >= .45f) tracker.Action = HandAction.Rest;
        else if (speed >= .035f && speed <= .34f) tracker.Action = HandAction.Pet;
        else tracker.Action = HandAction.Follow;
    }
}
