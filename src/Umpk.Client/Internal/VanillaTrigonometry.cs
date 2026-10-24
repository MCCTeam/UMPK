namespace Umpk.Client.Internal;

/// <summary>Native sine-table sampling with the narrow and wide index conversions.</summary>
internal static class VanillaTrigonometry
{
    public static (float Cos, float Sin) SinCos(float radians, bool wideIndex)
    {
        long sinIndex;
        long cosIndex;
        if (wideIndex)
        {
            double scaled = radians * 10430.378350470453;
            sinIndex = JavaLong(scaled);
            cosIndex = JavaLong(scaled + 16384.0);
        }
        else
        {
            float scaled = radians * 10430.378F;
            sinIndex = JavaInt(scaled);
            cosIndex = JavaInt(scaled + 16384.0F);
        }
        return (Sine.Values[(int)(cosIndex & 65535)], Sine.Values[(int)(sinIndex & 65535)]);
    }

    private static int JavaInt(float value)
        => float.IsNaN(value) ? 0 : value >= 2147483648.0F ? int.MaxValue : value <= -2147483648.0F ? int.MinValue : (int)value;

    private static long JavaLong(double value)
        => double.IsNaN(value) ? 0 : value >= 9223372036854775808.0 ? long.MaxValue : value <= -9223372036854775808.0 ? long.MinValue : (long)value;

    private static class Sine
    {
        internal static readonly float[] Values = Create();

        private static float[] Create()
        {
            var values = new float[65536];
            for (int index = 0; index < values.Length; index++)
                values[index] = (float)Math.Sin(index * Math.PI * 2.0 / 65536.0);
            return values;
        }
    }
}
