using UnityEngine;

namespace PortCCTV
{
    /// <summary>One frame of fly-camera input (already in camera terms, independent of the input device).</summary>
    public struct FlyInput
    {
        /// <summary>x = right (+) / left (-), y = forward (+) / back (-), each -1..1.</summary>
        public Vector2 move;
        /// <summary>+1 up, -1 down.</summary>
        public float vertical;
        /// <summary>Yaw / pitch change in degrees this frame (x = yaw to the right, y = pitch down).</summary>
        public Vector2 lookDeg;
        /// <summary>Mouse-wheel notches this frame (+ = faster).</summary>
        public float wheel;
        public bool fast, slow;
    }

    /// <summary>
    /// Free observation camera for the dev view (never used for captures). Game-style fly camera: WASD moves in the horizontal
    /// plane along the view heading (or along the view direction when <see cref="moveAlongView"/>), Q/E down/up, mouse look,
    /// wheel changes the base speed, Shift x4, Ctrl x0.25; velocity is smoothed so motion does not jerk. Pure state + Step so the
    /// behaviour is unit-tested without a window.
    /// </summary>
    public class DevFlyCam
    {
        public Vector3 position;
        public float yaw, pitch;
        public Vector3 velocity;
        public float baseSpeed = 40f;                  // m/s
        public bool moveAlongView;
        public float minY = float.NegativeInfinity;    // e.g. just above the water
        public const float MinSpeed = 2f, MaxSpeed = 800f, SpeedStep = 1.25f, Smoothing = 0.12f;

        public Quaternion Rotation => Quaternion.Euler(pitch, yaw, 0f);

        public void SetPose(Vector3 pos, float yawDeg, float pitchDeg)
        {
            position = pos; yaw = Mathf.Repeat(yawDeg, 360f); pitch = Mathf.Clamp(Normalize(pitchDeg), -89f, 89f); velocity = Vector3.zero;
            if (position.y < minY) position.y = minY;
        }

        public float CurrentSpeed(bool fast, bool slow) => baseSpeed * (fast ? 4f : 1f) * (slow ? 0.25f : 1f);

        public void Step(FlyInput i, float dt)
        {
            if (dt <= 0f) return;
            yaw = Mathf.Repeat(yaw + i.lookDeg.x, 360f);
            pitch = Mathf.Clamp(pitch + i.lookDeg.y, -89f, 89f);
            if (Mathf.Abs(i.wheel) > 1e-4f) baseSpeed = Mathf.Clamp(baseSpeed * Mathf.Pow(SpeedStep, i.wheel), MinSpeed, MaxSpeed);

            Vector3 fwd, right;
            if (moveAlongView) { var r = Rotation; fwd = r * Vector3.forward; right = r * Vector3.right; }
            else
            {
                float y = yaw * Mathf.Deg2Rad;
                fwd = new Vector3(Mathf.Sin(y), 0f, Mathf.Cos(y));
                right = new Vector3(Mathf.Cos(y), 0f, -Mathf.Sin(y));
            }
            var wish = fwd * Mathf.Clamp(i.move.y, -1f, 1f) + right * Mathf.Clamp(i.move.x, -1f, 1f) + Vector3.up * Mathf.Clamp(i.vertical, -1f, 1f);
            if (wish.sqrMagnitude > 1f) wish.Normalize();
            var target = wish * CurrentSpeed(i.fast, i.slow);
            velocity = Vector3.Lerp(velocity, target, 1f - Mathf.Exp(-dt / Smoothing));
            if (target.sqrMagnitude < 1e-6f && velocity.sqrMagnitude < 1e-4f) velocity = Vector3.zero;
            position += velocity * dt;
            if (position.y < minY) { position.y = minY; if (velocity.y < 0f) velocity.y = 0f; }
        }

        static float Normalize(float a) { a = Mathf.Repeat(a + 180f, 360f) - 180f; return a; }
    }
}
