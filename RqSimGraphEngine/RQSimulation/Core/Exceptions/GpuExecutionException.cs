using System;

namespace RQSimulation.Core.Exceptions;

/// <summary>
/// Exception thrown when a GPU operation fails during physics module execution.
/// Provides additional context about the GPU device and operation that failed.
/// </summary>
public class GpuExecutionException : Exception
{
    /// <summary>
    /// The ID of the GPU device where the error occurred.
    /// -1 indicates an unknown or default device.
    /// </summary>
    public int DeviceId { get; }

    /// <summary>
    /// The name of the physics module that was executing when the error occurred.
    /// </summary>
    public string? ModuleName { get; }

    /// <summary>
    /// The name of the GPU device (if available).
    /// </summary>
    public string? DeviceName { get; }

    /// <summary>
    /// Creates a new GPU execution exception with device context.
    /// </summary>
    /// <param name="message">Error message describing what went wrong</param>
    /// <param name="deviceId">ID of the GPU device where the error occurred</param>
    /// <param name="inner">The underlying exception that caused this error</param>
    public GpuExecutionException(string message, int deviceId, Exception? inner = null)
        : base(message, inner)
    {
        DeviceId = deviceId;
    }

    /// <summary>
    /// Creates a new GPU execution exception with full device and module context.
    /// </summary>
    /// <param name="message">Error message describing what went wrong</param>
    /// <param name="deviceId">ID of the GPU device where the error occurred</param>
    /// <param name="deviceName">Name of the GPU device (e.g., "NVIDIA RTX 3090")</param>
    /// <param name="moduleName">Name of the physics module that was executing</param>
    /// <param name="inner">The underlying exception that caused this error</param>
    public GpuExecutionException(
        string message,
        int deviceId,
        string? deviceName,
        string? moduleName,
        Exception? inner = null)
        : base(message, inner)
    {
        DeviceId = deviceId;
        DeviceName = deviceName;
        ModuleName = moduleName;
    }

    /// <summary>
    /// Returns a detailed message including device and module information.
    /// </summary>
    public override string ToString()
    {
        var baseMessage = base.ToString();
        var context = $"GPU Device: {DeviceId}";

        if (!string.IsNullOrEmpty(DeviceName))
        {
            context += $" ({DeviceName})";
        }

        if (!string.IsNullOrEmpty(ModuleName))
        {
            context += $", Module: {ModuleName}";
        }

        return $"{baseMessage}\n{context}";
    }
}
