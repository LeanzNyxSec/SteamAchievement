// SAM Enhanced modification, 2026. Distributed under the included zlib license.
namespace SAM.API.Callbacks
{
    public sealed class UserStatsStored : Callback<Types.UserStatsStored>
    {
        public override int Id => 1102;
        public override bool IsServer => false;
    }
}
