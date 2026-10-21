public interface IExhibitionInputTarget
{
    bool MousePractice { get; set; }
    void SetStatus(string value);
    void SetFrame(byte[] pixels,int width,int height,GunInput left,GunInput right,byte[] mask);
}
