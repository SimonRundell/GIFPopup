using System.Drawing.Imaging;

namespace GifPopup;

/// <summary>
/// A PictureBox that animates a multi-frame GIF via ImageAnimator, used both
/// for the small tile previews and the large popup. Safe to set Image to a
/// non-animated or null image.
/// </summary>
public sealed class AnimatedGifBox : PictureBox
{
    private bool _isAnimating;

    public AnimatedGifBox()
    {
        SizeMode = PictureBoxSizeMode.Zoom;
    }

    protected override void OnPaint(PaintEventArgs pe)
    {
        if (Image is not null && _isAnimating)
        {
            ImageAnimator.UpdateFrames(Image);
        }
        base.OnPaint(pe);
    }

    /// <summary>Starts (or restarts) animation for the currently set Image, if it is animated.</summary>
    public void StartAnimating()
    {
        if (Image is null || _isAnimating)
        {
            return;
        }

        if (ImageAnimator.CanAnimate(Image))
        {
            ImageAnimator.Animate(Image, OnFrameChanged);
            _isAnimating = true;
        }
    }

    /// <summary>Stops animation, if currently animating.</summary>
    public void StopAnimating()
    {
        if (!_isAnimating || Image is null)
        {
            return;
        }

        ImageAnimator.StopAnimate(Image, OnFrameChanged);
        _isAnimating = false;
    }

    private void OnFrameChanged(object? sender, EventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(Invalidate);
        }
        else
        {
            Invalidate();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopAnimating();
        }
        base.Dispose(disposing);
    }
}
