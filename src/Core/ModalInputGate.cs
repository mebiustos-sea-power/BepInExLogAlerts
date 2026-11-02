namespace BepInExLogAlerts.Core;

internal sealed class ModalInputGate
{
    private bool waitingForRelease;
    private int releasedFrame;
    internal bool Blocked { get; private set; }

    internal void Observe(int frame, bool open, bool inputHeld)
    {
        if (open)
        {
            Blocked = true;
            waitingForRelease = true;
        }
        else if (Blocked)
        {
            // Once the release frame is over, a fresh press belongs to the game.
            if (!waitingForRelease && frame > releasedFrame) Blocked = false;
            else if (inputHeld) waitingForRelease = true;
            else if (waitingForRelease)
            {
                waitingForRelease = false;
                releasedFrame = frame;
            }

        }
    }
}
