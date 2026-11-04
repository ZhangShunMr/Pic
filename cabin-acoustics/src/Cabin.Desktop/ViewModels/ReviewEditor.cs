using Cabin.Core.Domain;

namespace Cabin.Desktop.ViewModels;

public sealed class ReviewEditor
{
    public ReviewEditor(ReviewItem item, Action changed)
    {
        Id = item.Id;
        Target = new("复核对象", item.Target, changed, required: true, maximumLength: 200);
        Action = new("复核步骤", item.Action, changed, required: true, maximumLength: 1000);
        Owner = new("复核负责人", item.Owner, changed, maximumLength: 80);
        Result = new("复核结果", item.Result, changed, required: true, maximumLength: 1000);
    }

    public Guid Id { get; }
    public InputField Target { get; }
    public InputField Action { get; }
    public InputField Owner { get; }
    public InputField Result { get; }

    public ReviewItem Build()
    {
        foreach (var field in new[] { Target, Action, Owner, Result })
            field.EnsureValid();
        return new()
        {
            Id = Id,
            Target = Target.Text,
            Action = Action.Text,
            Owner = Owner.Text,
            Result = Result.Text
        };
    }
}
