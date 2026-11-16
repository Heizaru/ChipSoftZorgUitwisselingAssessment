using System.ComponentModel.DataAnnotations;

namespace ZorgUitwisseling.Api.Common.Validation;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class BsnAttribute() : ValidationAttribute("The {0} field must contain a valid nine-digit BSN.")
{

    public override bool IsValid(object? value)
    {
        if (value is null)
        {
            return true;
        }

        if (value is not string { Length: 9 } bsn)
        {
            return false;
        }

        var checksum = 0;
        var hasNonZeroDigit = false;

        for (var index = 0; index < bsn.Length; index++)
        {
            if (bsn[index] is < '0' or > '9')
            {
                return false;
            }

            int digit = bsn[index] - '0';
            hasNonZeroDigit |= digit != 0;
            checksum += index == 8 ? -digit : (9 - index) * digit;
        }

        return hasNonZeroDigit && checksum % 11 == 0;
    }
}
