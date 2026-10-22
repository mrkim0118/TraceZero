namespace SecureFileEraser.Models;

public sealed record DeletePassOption(int PassCount, string Label)
{
    public override string ToString() => Label;
}
