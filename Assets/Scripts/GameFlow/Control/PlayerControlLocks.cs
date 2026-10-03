using System;
using System.Collections.Generic;
using UnityEngine;

namespace Purgers.GameFlow.Control
{
    [Flags]
    public enum PlayerControlMask
    {
        None = 0,
        Movement = 1,
        Look = 2,
        AllInput = 4,
        Gameplay = 8
    }

    /// <summary>Each acquisition has its own identity, even when reasons match.</summary>
    public sealed class PlayerControlLocks
    {
        private readonly Dictionary<Lease, PlayerControlMask> leases =
            new Dictionary<Lease, PlayerControlMask>();
        public PlayerControlMask Mask { get; private set; }
        public int Count => leases.Count;
        public event Action Changed;

        public IDisposable Acquire(PlayerControlMask mask, string reason)
        {
            if (mask == PlayerControlMask.None)
                throw new ArgumentException("A lock must block at least one control.", nameof(mask));
            var lease = new Lease(this, reason);
            leases.Add(lease, mask);
            Recalculate();
            return lease;
        }

        private void Release(Lease lease)
        {
            if (leases.Remove(lease))
                Recalculate();
        }

        private void Recalculate()
        {
            Mask = PlayerControlMask.None;
            foreach (PlayerControlMask mask in leases.Values)
                Mask |= mask;
            Changed?.Invoke();
        }

        public static bool BlocksMovement(PlayerControlMask mask) =>
            (mask & (PlayerControlMask.Movement | PlayerControlMask.AllInput)) != 0;

        public static bool BlocksLook(PlayerControlMask mask) =>
            (mask & (PlayerControlMask.Look | PlayerControlMask.AllInput)) != 0;

        public static bool BlocksGameplay(PlayerControlMask mask) =>
            (mask & (PlayerControlMask.Gameplay | PlayerControlMask.AllInput)) != 0;

        public static void Filter(ref NetInput input, PlayerControlMask mask)
        {
            input.BlockedControls |= mask;
            mask = input.BlockedControls;
            if (BlocksGameplay(mask))
            {
                input.Buttons = default;
                input.Direction = Vector2.zero;
                input.LookDelta = Vector2.zero;
                return;
            }
            if (BlocksLook(mask))
                input.LookDelta = Vector2.zero;
            if (BlocksMovement(mask))
            {
                input.Direction = Vector2.zero;
                input.Buttons.Set(InputButton.Jump, false);
                input.Buttons.Set(InputButton.Sprint, false);
                input.Buttons.Set(InputButton.Crouch, false);
                input.Buttons.Set(InputButton.Grapple, false);
                // QuickAction can start a Tank dash; movement locks must prevent it.
                input.Buttons.Set(InputButton.QuickAction, false);
            }
        }

        private sealed class Lease : IDisposable
        {
            private PlayerControlLocks owner;
            public readonly string Reason;
            public Lease(PlayerControlLocks owner, string reason)
            {
                this.owner = owner;
                Reason = reason;
            }
            public void Dispose()
            {
                PlayerControlLocks previous = owner;
                owner = null;
                previous?.Release(this);
            }
        }
    }
}
