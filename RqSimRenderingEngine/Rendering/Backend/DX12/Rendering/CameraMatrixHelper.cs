using System.Numerics;

namespace RqSimRenderingEngine.Rendering.Backend.DX12.Rendering;

/// <summary>
/// Provides camera matrix utilities optimized for DX12 rendering.
/// Includes Reverse-Z projection for improved depth precision.
/// All matrices use left-handed coordinate system standard for DirectX.
/// </summary>
public static class CameraMatrixHelper
{
    /// <summary>
    /// Creates a Reverse-Z perspective projection matrix (left-handed for DirectX).
    /// Maps near plane to depth 1.0 and far plane to depth 0.0.
    /// </summary>
    /// <param name="fovY">Vertical field of view in radians.</param>
    /// <param name="aspectRatio">Width / Height aspect ratio.</param>
    /// <param name="nearPlane">Near clipping plane distance (must be positive).</param>
    /// <param name="farPlane">Far clipping plane distance (must be greater than near, use float.PositiveInfinity for infinite far).</param>
    /// <returns>Reverse-Z perspective projection matrix for DX12 (left-handed, row-major).</returns>
    public static Matrix4x4 CreatePerspectiveReverseZ(float fovY, float aspectRatio, float nearPlane, float farPlane)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(nearPlane);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(farPlane, nearPlane);

        float yScale = 1.0f / MathF.Tan(fovY * 0.5f);
        float xScale = yScale / aspectRatio;

        // Reverse-Z with infinite far plane for maximum precision (left-handed)
        // In left-handed system, camera looks down +Z, so we use M34 = +1
        if (float.IsPositiveInfinity(farPlane))
        {
            // Infinite far plane Reverse-Z (left-handed)
            // Near plane maps to 1.0, infinity maps to 0.0
            return new Matrix4x4(
                xScale, 0, 0, 0,
                0, yScale, 0, 0,
                0, 0, 0, 1,        // M34 = +1 for left-handed
                0, 0, nearPlane, 0 // M43 = nearPlane (maps near to depth 1.0)
            );
        }

        // Finite far plane Reverse-Z (left-handed)
        // 
        // For row-vector multiplication (v * M), with v = (x, y, z, 1):
        //   clip.z = z * M33 + 1 * M43
        //   clip.w = z * M34 + 1 * M44 = z * 1 + 0 = z
        //   ndc.z = clip.z / clip.w = (z * M33 + M43) / z
        //
        // Requirements for Reverse-Z:
        //   At z=near: ndc.z = 1.0  =>  (near * M33 + M43) / near = 1
        //   At z=far:  ndc.z = 0.0  =>  (far * M33 + M43) / far = 0
        //
        // From far equation: far * M33 + M43 = 0  =>  M43 = -far * M33
        // Substitute into near equation:
        //   (near * M33 - far * M33) / near = 1
        //   M33 * (near - far) / near = 1
        //   M33 = near / (near - far)
        //
        // Therefore:
        //   M33 = near / (near - far) = -near / (far - near)
        //   M43 = -far * M33 = -far * near / (near - far) = far * near / (far - near)
        //
        float range = farPlane - nearPlane;
        
        return new Matrix4x4(
            xScale, 0, 0, 0,
            0, yScale, 0, 0,
            0, 0, -nearPlane / range, 1,          // M33 = -n/(f-n) for Reverse-Z LH
            0, 0, nearPlane * farPlane / range, 0 // M43 = n*f/(f-n) for Reverse-Z LH
        );
    }

    /// <summary>
    /// Creates a standard perspective projection matrix (right-handed).
    /// Maps near plane to depth 0.0 and far plane to depth 1.0.
    /// </summary>
    /// <param name="fovY">Vertical field of view in radians.</param>
    /// <param name="aspectRatio">Width / Height aspect ratio.</param>
    /// <param name="nearPlane">Near clipping plane distance.</param>
    /// <param name="farPlane">Far clipping plane distance.</param>
    /// <returns>Standard perspective projection matrix (right-handed).</returns>
    public static Matrix4x4 CreatePerspectiveStandard(float fovY, float aspectRatio, float nearPlane, float farPlane)
    {
        return Matrix4x4.CreatePerspectiveFieldOfView(fovY, aspectRatio, nearPlane, farPlane);
    }

    /// <summary>
    /// Creates a look-at view matrix (left-handed coordinate system for DirectX).
    /// </summary>
    /// <param name="cameraPosition">Camera world position.</param>
    /// <param name="targetPosition">Look-at target position.</param>
    /// <param name="upVector">Up vector (typically Y-up).</param>
    /// <returns>View matrix (left-handed).</returns>
    public static Matrix4x4 CreateLookAt(Vector3 cameraPosition, Vector3 targetPosition, Vector3 upVector)
    {
        // Use left-handed version for DirectX compatibility
        return Matrix4x4.CreateLookAtLeftHanded(cameraPosition, targetPosition, upVector);
    }

    /// <summary>
    /// Creates an orbit camera view matrix (left-handed for DirectX).
    /// </summary>
    /// <param name="target">Target position to orbit around.</param>
    /// <param name="distance">Distance from target.</param>
    /// <param name="yaw">Horizontal rotation in radians.</param>
    /// <param name="pitch">Vertical rotation in radians (clamped to avoid gimbal lock).</param>
    /// <returns>View matrix for orbit camera (left-handed).</returns>
    public static Matrix4x4 CreateOrbitCamera(Vector3 target, float distance, float yaw, float pitch)
    {
        // Clamp pitch to avoid gimbal lock
        const float maxPitch = MathF.PI / 2 - 0.01f;
        pitch = Math.Clamp(pitch, -maxPitch, maxPitch);

        float cosPitch = MathF.Cos(pitch);
        float sinPitch = MathF.Sin(pitch);
        float cosYaw = MathF.Cos(yaw);
        float sinYaw = MathF.Sin(yaw);

        // In left-handed system, +Z goes into the screen (away from viewer)
        // Camera orbits around target at specified distance
        // With yaw=0, pitch=0: camera is at (0, 0, -distance) looking at target
        Vector3 cameraPosition = target + new Vector3(
            distance * cosPitch * sinYaw,
            distance * sinPitch,
            -distance * cosPitch * cosYaw  // Negative Z puts camera in front of target (facing +Z)
        );

        return Matrix4x4.CreateLookAtLeftHanded(cameraPosition, target, Vector3.UnitY);
    }

    /// <summary>
    /// Gets the clear depth value for Reverse-Z (0.0 = far plane).
    /// </summary>
    public static float ReverseZClearDepth => 0.0f;

    /// <summary>
    /// Gets the clear depth value for standard Z (1.0 = far plane).
    /// </summary>
    public static float StandardClearDepth => 1.0f;
}
