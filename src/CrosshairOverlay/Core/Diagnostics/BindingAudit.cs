using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Microsoft.Extensions.Logging;

namespace CrosshairOverlay.Core.Diagnostics;

/// <summary>Opt-in audit of materialized WPF bindings; no tree walk in normal launches.</summary>
internal static class BindingAudit
{
    public static bool Enabled => Environment.GetEnvironmentVariable("CROSSGOVERLAY_BINDING_AUDIT") == "1";

    public static int Scan(DependencyObject root, ILogger? logger = null)
    {
        var errors = 0;
        Visit(root);
        return errors;

        void Visit(DependencyObject node)
        {
            var values = node.GetLocalValueEnumerator();
            while (values.MoveNext())
            {
                var property = values.Current.Property;
                var expression = BindingOperations.GetBindingExpressionBase(node, property);
                if (expression?.Status is not (BindingStatus.PathError or BindingStatus.UpdateTargetError
                    or BindingStatus.UpdateSourceError)) continue;

                errors++;
                var binding = BindingOperations.GetBindingBase(node, property);
                var path = (binding as Binding)?.Path?.Path ?? binding?.GetType().Name ?? "?";
                logger?.LogWarning("BindingAudit {Status}: {Target}.{Property} <- {Path}; DataContext={Context}",
                    expression.Status, node.GetType().Name, property.Name, path,
                    (node as FrameworkElement)?.DataContext?.GetType().Name ?? "null");
            }

            if (node is not Visual and not Visual3D) return;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                Visit(VisualTreeHelper.GetChild(node, i));
        }
    }
}
