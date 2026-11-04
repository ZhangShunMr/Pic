using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TrimContact.Core.Analysis;
using TrimContact.Core.Domain;

namespace TrimContact.Desktop.Controls;

public sealed class LayoutCanvas : WorkspaceDrawing
{
    private double minimumX;
    private double minimumY;
    private double scale = 1;
    private double left;
    private double bottom;
    private readonly List<(Guid Id, Point Pixel)> hitTargets = [];
    private bool drawable;
    private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(32, 59, 67));
    private static readonly Brush Blue = new SolidColorBrush(Color.FromRgb(58, 126, 170));
    private static readonly Brush Green = new SolidColorBrush(Color.FromRgb(30, 139, 109));
    private static readonly Brush Orange = new SolidColorBrush(Color.FromRgb(194, 123, 30));
    private static readonly Brush Red = new SolidColorBrush(Color.FromRgb(182, 54, 58));

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        hitTargets.Clear();
        drawable = false;
        dc.DrawRectangle(Brushes.White, new Pen(Brushes.LightGray, 1), new Rect(RenderSize));
        if (Workspace is null || ActualWidth < 120 || ActualHeight < 100) return;
        var panel = Workspace.Draft.Panel;
        var vertices = panel.Outline.Where(v => Finite(v.Xmm, v.Ymm)).ToArray();
        if (vertices.Length < 3)
        {
            Text(dc, "空白布置：先建立饰板轮廓", 18, 30, Ink, 16);
            Text(dc, "图为输入示意，不是测量证据", 18, 58);
            return;
        }
        minimumX = vertices.Min(v => v.Xmm);
        minimumY = vertices.Min(v => v.Ymm);
        var width = Math.Max(1, vertices.Max(v => v.Xmm) - minimumX);
        var height = Math.Max(1, vertices.Max(v => v.Ymm) - minimumY);
        if (!Finite(width, height)) return;
        scale = Math.Min((ActualWidth - 100) / width, (ActualHeight - 75) / height);
        left = (ActualWidth - width * scale) / 2;
        bottom = ActualHeight - 38;
        drawable = true;
        for (int i = 0; i <= 4; i++)
        {
            var x = left + width * scale * i / 4;
            var y = bottom - height * scale * i / 4;
            dc.DrawLine(new Pen(Brushes.WhiteSmoke, 1), new Point(x, bottom), new Point(x, bottom - height * scale));
            dc.DrawLine(new Pen(Brushes.WhiteSmoke, 1), new Point(left, y), new Point(left + width * scale, y));
        }
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(Pixel(vertices[0].Xmm, vertices[0].Ymm), true, true);
            context.PolyLineTo(vertices.Skip(1).Select(v => Pixel(v.Xmm, v.Ymm)).ToArray(), true, false);
        }
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(65, 164, 204, 199)), new Pen(Ink, 1.5), geometry);
        Text(dc, "X → mm / Y ↑ mm · " + Workspace.Draft.Origin.Label(), 10, 7, Ink);
        Text(dc, N(minimumX) + ", " + N(minimumY), left, bottom + 5);
        Text(dc, N(width) + " × " + N(height) + " mm", left + 90, bottom + 5);
        if (Finite(panel.CenterXmm, panel.CenterYmm))
        {
            var center = Pixel(panel.CenterXmm, panel.CenterYmm);
            dc.DrawLine(new Pen(Ink, 1), new Point(center.X - 7, center.Y), new Point(center.X + 7, center.Y));
            dc.DrawLine(new Pen(Ink, 1), new Point(center.X, center.Y - 7), new Point(center.X, center.Y + 7));
        }
        var variant = Workspace.SelectedVariant;
        if (variant is null) return;
        var envelopes = Workspace.Envelopes.ToDictionary(e => e.PointId);
        foreach (var clip in variant.Clips)
            DrawPoint(dc, clip.Id, clip.Name, clip.Xmm, clip.Ymm, true, envelopes.GetValueOrDefault(clip.Id));
        foreach (var contact in variant.Contacts)
            DrawPoint(dc, contact.Id, contact.Name, contact.Xmm, contact.Ymm, false, envelopes.GetValueOrDefault(contact.Id));
    }

    private Point Pixel(double x, double y) => new(left + (x - minimumX) * scale, bottom - (y - minimumY) * scale);

    private void DrawPoint(DrawingContext dc, Guid id, string name, double x, double y, bool clip, PointEnvelope? envelope)
    {
        if (!Finite(x, y)) return;
        var position = Pixel(x, y);
        var brush = envelope is null ? Blue : envelope.MaxUtilization > 1 ? Red
            : !clip && envelope.MinCompressionMm <= Core.Domain.Product.ContactEpsilon ? Orange : Green;
        var selected = clip ? Workspace?.SelectedClip?.Id == id : Workspace?.SelectedContact?.Id == id;
        var pen = new Pen(selected ? Brushes.Black : Brushes.White, selected ? 2 : 1);
        if (clip) dc.DrawRectangle(brush, pen, new Rect(position.X - 6, position.Y - 6, 12, 12));
        else dc.DrawEllipse(brush, pen, position, 7, 7);
        Text(dc, name + (envelope is null ? "" : " · " + envelope.State), position.X + 9, position.Y - 8, Ink, 11);
        hitTargets.Add((id, position));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!drawable || Workspace is null || !Workspace.CanEdit) return;
        var position = e.GetPosition(this);
        var nearest = hitTargets.OrderBy(t => (t.Pixel - position).LengthSquared).FirstOrDefault();
        Guid? id = nearest.Id != Guid.Empty && (nearest.Pixel - position).Length < 16 ? nearest.Id : null;
        Workspace.PlaceAt(minimumX + (position.X - left) / scale, minimumY + (bottom - position.Y) / scale, id);
        InvalidateVisual();
        e.Handled = true;
    }
}
