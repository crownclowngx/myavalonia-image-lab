namespace ImageLabPlugin.Infrastructure.ErrorCorrection;

/// <summary>GF(256) 有限域，实现 Reed-Solomon 所需的加、乘、逆和多项式运算。</summary>
/// <remarks>
/// V1 使用常见的本原多项式 0x11D。有限域加减在特征二下都等于 XOR；把这条规则集中在这里，
/// 可以避免编码器、解码器各自维护容易漂移的位运算实现。
/// </remarks>
internal sealed class GaloisField256
{
    public static GaloisField256 Instance { get; } = new();

    private readonly int[] _exponents = new int[512];
    private readonly int[] _logarithms = new int[256];

    private GaloisField256()
    {
        var value = 1;
        for (var i = 0; i < 255; i++)
        {
            _exponents[i] = value;
            _logarithms[value] = i;
            value <<= 1;
            if ((value & 0x100) != 0)
            {
                value ^= 0x11D;
            }
        }

        // 乘法只会访问两个对数之和，复制一次表可去掉热路径中的取模。
        for (var i = 255; i < _exponents.Length; i++)
        {
            _exponents[i] = _exponents[i - 255];
        }

        Zero = new GfPolynomial(this, [0]);
        One = new GfPolynomial(this, [1]);
    }

    public int Size => 256;
    public int GeneratorBase => 0;
    public GfPolynomial Zero { get; }
    public GfPolynomial One { get; }

    public int Exp(int exponent) => _exponents[exponent];

    public int Log(int value)
    {
        if (value == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "零没有有限域对数。");
        }

        return _logarithms[value];
    }

    public int Inverse(int value)
    {
        if (value == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "零没有乘法逆元。");
        }

        return _exponents[255 - _logarithms[value]];
    }

    public int Multiply(int left, int right) =>
        left == 0 || right == 0 ? 0 : _exponents[_logarithms[left] + _logarithms[right]];

    public GfPolynomial BuildMonomial(int degree, int coefficient)
    {
        if (degree < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(degree));
        }

        if (coefficient == 0)
        {
            return Zero;
        }

        var coefficients = new int[degree + 1];
        coefficients[0] = coefficient;
        return new GfPolynomial(this, coefficients);
    }
}

/// <summary>系数按最高次项到常数项排列的 GF(256) 多项式。</summary>
internal sealed class GfPolynomial
{
    private readonly int[] _coefficients;

    public GfPolynomial(GaloisField256 field, ReadOnlySpan<int> coefficients)
    {
        Field = field ?? throw new ArgumentNullException(nameof(field));
        if (coefficients.IsEmpty)
        {
            throw new ArgumentException("多项式至少需要一个系数。", nameof(coefficients));
        }

        var firstNonZero = 0;
        while (firstNonZero < coefficients.Length - 1 && coefficients[firstNonZero] == 0)
        {
            firstNonZero++;
        }

        _coefficients = coefficients[firstNonZero..].ToArray();
    }

    public GaloisField256 Field { get; }
    public int Degree => _coefficients.Length - 1;
    public bool IsZero => _coefficients[0] == 0;
    public ReadOnlySpan<int> Coefficients => _coefficients;

    public int GetCoefficient(int degree) => _coefficients[_coefficients.Length - 1 - degree];

    public int EvaluateAt(int value)
    {
        if (value == 0)
        {
            return GetCoefficient(0);
        }

        if (value == 1)
        {
            var sum = 0;
            foreach (var coefficient in _coefficients)
            {
                sum ^= coefficient;
            }

            return sum;
        }

        var result = _coefficients[0];
        for (var i = 1; i < _coefficients.Length; i++)
        {
            result = Field.Multiply(result, value) ^ _coefficients[i];
        }

        return result;
    }

    public GfPolynomial AddOrSubtract(GfPolynomial other)
    {
        EnsureSameField(other);
        if (IsZero)
        {
            return other;
        }

        if (other.IsZero)
        {
            return this;
        }

        var smaller = _coefficients;
        var larger = other._coefficients;
        if (smaller.Length > larger.Length)
        {
            (smaller, larger) = (larger, smaller);
        }

        var sum = (int[])larger.Clone();
        var offset = larger.Length - smaller.Length;
        for (var i = 0; i < smaller.Length; i++)
        {
            sum[i + offset] ^= smaller[i];
        }

        return new GfPolynomial(Field, sum);
    }

    public GfPolynomial Multiply(GfPolynomial other)
    {
        EnsureSameField(other);
        if (IsZero || other.IsZero)
        {
            return Field.Zero;
        }

        var product = new int[_coefficients.Length + other._coefficients.Length - 1];
        for (var i = 0; i < _coefficients.Length; i++)
        {
            for (var j = 0; j < other._coefficients.Length; j++)
            {
                product[i + j] ^= Field.Multiply(_coefficients[i], other._coefficients[j]);
            }
        }

        return new GfPolynomial(Field, product);
    }

    public GfPolynomial Multiply(int scalar)
    {
        if (scalar == 0)
        {
            return Field.Zero;
        }

        if (scalar == 1)
        {
            return this;
        }

        var product = new int[_coefficients.Length];
        for (var i = 0; i < _coefficients.Length; i++)
        {
            product[i] = Field.Multiply(_coefficients[i], scalar);
        }

        return new GfPolynomial(Field, product);
    }

    public GfPolynomial MultiplyByMonomial(int degree, int coefficient)
    {
        if (degree < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(degree));
        }

        if (coefficient == 0)
        {
            return Field.Zero;
        }

        var product = new int[_coefficients.Length + degree];
        for (var i = 0; i < _coefficients.Length; i++)
        {
            product[i] = Field.Multiply(_coefficients[i], coefficient);
        }

        return new GfPolynomial(Field, product);
    }

    public GfPolynomial Remainder(GfPolynomial divisor)
    {
        EnsureSameField(divisor);
        if (divisor.IsZero)
        {
            throw new DivideByZeroException("不能用零多项式做 Reed-Solomon 除法。");
        }

        var remainder = this;
        var denominatorLeadingTerm = divisor.GetCoefficient(divisor.Degree);
        var inverseDenominatorLeadingTerm = Field.Inverse(denominatorLeadingTerm);
        while (remainder.Degree >= divisor.Degree && !remainder.IsZero)
        {
            var degreeDifference = remainder.Degree - divisor.Degree;
            var scale = Field.Multiply(remainder.GetCoefficient(remainder.Degree), inverseDenominatorLeadingTerm);
            remainder = remainder.AddOrSubtract(divisor.MultiplyByMonomial(degreeDifference, scale));
        }

        return remainder;
    }

    private void EnsureSameField(GfPolynomial other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (!ReferenceEquals(Field, other.Field))
        {
            throw new ArgumentException("多项式不属于同一个有限域。", nameof(other));
        }
    }
}
