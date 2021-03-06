﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Pchp.Core.Dynamic
{
    /// <summary>
    /// Methods for selecting best method overload from possible candidates.
    /// </summary>
    internal static class OverloadResolver
    {
        /// <summary>
        /// Selects all method candidates.
        /// </summary>
        public static IEnumerable<MethodBase> SelectCandidates(this Type type)
        {
            return type.GetRuntimeMethods();
        }

        /// <summary>
        /// Selects only candidates of given name.
        /// </summary>
        public static IEnumerable<MethodBase> SelectByName(this IEnumerable<MethodBase> candidates, string name)
        {
            return candidates.Where(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Selects only candidates visible from the current class context.
        /// </summary>
        public static bool IsVisible(this MethodBase m, Type classCtx)
        {
            if (m.IsPrivate && m.DeclaringType != classCtx)
            {
                return false;
            }

            if (m.IsFamily)
            {
                if (classCtx == null)
                {
                    return false;
                }

                var m_type = m.DeclaringType.GetTypeInfo();
                var classCtx_type = classCtx.GetTypeInfo();

                if (!classCtx_type.IsAssignableFrom(m_type) && !m_type.IsAssignableFrom(classCtx_type))
                {
                    return false;
                }
            }

            return true;
        }

        public static IEnumerable<MethodBase> SelectVisible(this IEnumerable<MethodBase> candidates, Type classCtx)
        {
            if (classCtx == null)
            {
                return candidates.Where(m => m.IsPublic);
            }

            return candidates.Where(m => m.IsVisible(classCtx));
        }

        /// <summary>
        /// Selects only static methods.
        /// </summary>
        public static IEnumerable<MethodBase> SelectStatic(this IEnumerable<MethodBase> candidates)
        {
            return candidates.Where(m => m.IsStatic);
        }

        /// <summary>
        /// Gets value indicating the parameter is a special late static bound parameter.
        /// </summary>
        static bool IsStaticBoundParameter(ParameterInfo p)
        {
            return p.ParameterType == typeof(Type) && p.Name == "<static>";
        }

        /// <summary>
        /// Gets value indicating the parameter is a special local parameters parameter.
        /// </summary>
        static bool IsLocalsParameter(ParameterInfo p)
        {
            return p.ParameterType == typeof(PhpArray) && p.Name == "<locals>";
        }
    }
}
