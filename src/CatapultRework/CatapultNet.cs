using UnityEngine;

namespace CatapultRework
{
    /// <summary>
    /// Added to every catapult. Carries the mod's network messages:
    ///  - Fire(target): sent by the aiming player to everybody. Every client stores the target and
    ///    starts the vanilla shot; the catapult owner (whoever owns its ZDO, possibly a teammate who
    ///    loaded it) spawns the projectile with the solved velocity.
    ///  - Aimer(playerId): one-player-at-a-time lock with a heartbeat, so a disconnected aimer
    ///    never blocks the catapult for long.
    /// </summary>
    public class CatapultNet : MonoBehaviour
    {
        public const string RpcFire = "CatapultRework_Fire";
        public const string RpcAimer = "CatapultRework_Aimer";
        public const float AimerLockTimeout = 3f;
        public const float AimerHeartbeat = 1f;

        public Catapult Catapult { get; private set; }
        public ZNetView NView { get; private set; }

        public bool HasPendingTarget;
        public Vector3 PendingTarget;

        public bool HasCachedLaunchOffset;
        public Vector3 CachedLaunchOffset;

        private long _aimerId;
        private float _aimerSeen;

        internal void Init(Catapult c, ZNetView nview)
        {
            Catapult = c;
            NView = nview;
            if (NView == null || !NView.IsValid()) return;
            NView.Register<Vector3>(RpcFire, RPC_Fire);
            NView.Register<long>(RpcAimer, RPC_Aimer);
        }

        public bool IsAimedByOther(long myId) =>
            _aimerId != 0 && _aimerId != myId && Time.time - _aimerSeen < AimerLockTimeout;

        public void SendAimer(long playerId)
        {
            if (NView != null && NView.IsValid())
                NView.InvokeRPC(ZNetView.Everybody, RpcAimer, playerId);
        }

        public void SendFire(Vector3 target)
        {
            if (NView != null && NView.IsValid())
                NView.InvokeRPC(ZNetView.Everybody, RpcFire, target);
        }

        private void RPC_Aimer(long sender, long playerId)
        {
            if (playerId != _aimerId) ModLog.Debug($"Aimer lock: {(playerId == 0 ? "released" : "taken by player " + playerId)}");
            _aimerId = playerId;
            _aimerSeen = Time.time;
        }

        private void RPC_Fire(long sender, Vector3 target)
        {
            if (Catapult == null) return;
            // Ignore if the arm is already moving (double click, lag) or nothing is loaded here.
            if (Catapult.m_armAnimTime != 0f || Catapult.m_loadedItem == null)
            {
                ModLog.Debug($"Fire RPC from {sender} ignored: armAnimTime={ModLog.F(Catapult.m_armAnimTime)}, loaded={(Catapult.m_loadedItem != null)}");
                return;
            }
            ModLog.Debug($"Fire RPC from {sender}: target {ModLog.F(target)}");
            PendingTarget = target;
            HasPendingTarget = true;
            Catapult.CancelInvoke("Shoot");
            // Same as vanilla Shoot() delivering RPC_Shoot to everybody.
            Catapult.RPC_Shoot(sender);
        }
    }
}
