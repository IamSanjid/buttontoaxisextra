using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HidWizards.UCR.Plugins.Remapper
{
    public enum CurveMode { Gamma, Smoothstep, TwoStage, GammaExponential, SkewedS }

    public static class CurveModeExtension
    {
        public static bool TryToCurveMode(this int mode, out CurveMode result)
        {
            switch (mode)
            {
                case 0:
                    result = CurveMode.Gamma;
                    return true;
                case 1:
                    result = CurveMode.Smoothstep;
                    return true;
                case 2:
                    result = CurveMode.TwoStage;
                    return true;
                case 3:
                    result = CurveMode.GammaExponential;
                    return true;
                case 4:
                    result = CurveMode.SkewedS;
                    return true;
            }

            result = CurveMode.Gamma;
            return false;
        }
    }
}
