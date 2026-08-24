namespace SecondView;

public sealed class ModConfig
{
    public int WindowWidth { get; set; } = 1080;
    public int WindowHeight { get; set; } = 1312;
    public int WindowX { get; set; } = int.MinValue;
    public int WindowY { get; set; } = int.MinValue;
    public float Zoom { get; set; } = 0.75f;
    public float UiScale { get; set; } = 0.75f;
    public int FrameInterval { get; set; } = 1;
    public int HudLift { get; set; } = 112;
    public bool ShowHud { get; set; } = false;
}
