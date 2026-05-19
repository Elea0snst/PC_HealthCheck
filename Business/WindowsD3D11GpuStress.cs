using System.Text;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Device = Vortice.Direct3D11.ID3D11Device;

namespace PC_HealthCheck.Business;

/// <summary>D3D11 compute-нагрузка на дискретную или встроенную GPU (Windows).</summary>
internal static class WindowsD3D11GpuStress
{
    private const string ShaderSource = """
        RWStructuredBuffer<float> buf : register(u0);
        [numthreads(256, 1, 1)]
        void CSMain(uint3 id : SV_DispatchThreadID)
        {
            uint i = id.x;
            float x = (float)(i & 1023u) * 0.001f + 1.0f;
            [loop]
            for (int k = 0; k < 4096; k++)
                x = sin(x) * cos(x) + sqrt(abs(x) + 1.0f);
            buf[i] = x;
        }
        """;

    public static (string Mode, bool UsedDiscreteGpu) Run(TimeSpan duration, CancellationToken ct)
    {
        D3D11.D3D11CreateDevice(
            null,
            DriverType.Hardware,
            DeviceCreationFlags.None,
            null!,
            out Device device,
            out ID3D11DeviceContext context).CheckError();

        try
        {
            var bytecode = CompileShader(ShaderSource);
            using var cs = device.CreateComputeShader(bytecode);
            context.CSSetShader(cs);

            const uint elementCount = 256 * 1024;
            const uint byteWidth = elementCount * sizeof(float);
            using var buffer = device.CreateBuffer(new BufferDescription
            {
                ByteWidth = (int)byteWidth,
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource,
                CPUAccessFlags = CpuAccessFlags.None,
                MiscFlags = ResourceOptionFlags.BufferStructured,
                StructureByteStride = sizeof(float)
            });

            using var uav = device.CreateUnorderedAccessView(buffer, new UnorderedAccessViewDescription
            {
                Format = Format.Unknown,
                ViewDimension = UnorderedAccessViewDimension.Buffer,
                Buffer = new BufferUnorderedAccessView
                {
                    FirstElement = 0,
                    NumElements = elementCount
                }
            });

            context.CSSetUnorderedAccessView(0, uav);

            var end = DateTime.UtcNow + duration;
            var groups = elementCount / 256;
            var usedDiscrete = IsLikelyDiscreteGpu(device);

            while (!ct.IsCancellationRequested && DateTime.UtcNow < end)
            {
                context.Dispatch(groups, 1, 1);
                context.Flush();
            }

            var mode = usedDiscrete
                ? "D3D11 Compute (дискретная/основная GPU)"
                : "D3D11 Compute (встроенная GPU)";
            return (mode, usedDiscrete);
        }
        finally
        {
            context.ClearState();
            context.Dispose();
            device.Dispose();
        }
    }

    private static byte[] CompileShader(string source)
    {
        var bytes = Encoding.UTF8.GetBytes(source);
        unsafe
        {
            fixed (byte* ptr = bytes)
            {
                Compiler.Compile(
                    ptr,
                    (nuint)bytes.Length,
                    "stress.hlsl",
                    null,
                    null,
                    "CSMain",
                    "cs_5_0",
                    ShaderFlags.OptimizationLevel3,
                    EffectFlags.None,
                    out Blob code,
                    out Blob? errors).CheckError();

                if (errors is not null)
                {
                    var msg = errors.AsString() ?? "Shader compile error";
                    errors.Dispose();
                    throw new InvalidOperationException(msg);
                }

                var result = code.AsSpan().ToArray();
                code.Dispose();
                return result;
            }
        }
    }

    private static bool IsLikelyDiscreteGpu(Device device)
    {
        try
        {
            using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
            dxgiDevice.GetAdapter(out var adapter);
            var desc = adapter.Description;
            return !desc.Description.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true;
        }
    }
}
