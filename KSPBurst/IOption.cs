using JetBrains.Annotations;

namespace KSPBurst
{
    public interface IOption
    {
        [NotNull] string Name { get; }

        [CanBeNull]
        string MakeOption([CanBeNull] string value);
    }
}