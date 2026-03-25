using System.Runtime.Intrinsics.X86;



namespace PC_HealthCheck.DAL;



internal static class CpuFeatureDetector

{

    /// <summary>CPU instruction extensions visible to the current .NET process (matches OS + CPU).</summary>

    public static string SummarizeX86()

    {

        var parts = new List<string>(32);

        void add(string name, bool ok)

        {

            if (ok) parts.Add(name);

        }



        add("SSE", Sse.IsSupported);

        add("SSE2", Sse2.IsSupported);

        add("SSE3", Sse3.IsSupported);

        add("SSSE3", Ssse3.IsSupported);

        add("SSE4.1", Sse41.IsSupported);

        add("SSE4.2", Sse42.IsSupported);

        add("AES-NI", Aes.IsSupported);

        add("PCLMULQDQ", Pclmulqdq.IsSupported);

        add("AVX", Avx.IsSupported);

        add("AVX2", Avx2.IsSupported);

        add("FMA", Fma.IsSupported);

        add("BMI1", Bmi1.IsSupported);

        add("BMI2", Bmi2.IsSupported);

        add("LZCNT", Lzcnt.IsSupported);

        add("POPCNT", Popcnt.IsSupported);

        add("AVX512F", Avx512F.IsSupported);

        add("AVX512BW", Avx512BW.IsSupported);

        add("AVX512VL", Avx512F.VL.IsSupported);



        return parts.Count == 0 ? "N/A" : string.Join(", ", parts);

    }

}


