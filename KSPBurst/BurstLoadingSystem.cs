using JetBrains.Annotations;

namespace KSPBurst
{
    public class BurstLoadingSystem : LoadingSystem
    {
        public override bool IsReady()
        {
            return KSPBurst.Status is KSPBurst.CompilerStatus.Completed or KSPBurst.CompilerStatus.Error;
        }

        [NotNull]
        public override string ProgressTitle()
        {
            return $"{PathUtil.ModName}: waiting for Burst compiler...";
        }
    }
}