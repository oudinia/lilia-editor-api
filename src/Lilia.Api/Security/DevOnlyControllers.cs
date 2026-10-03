using System.Reflection;
using Lilia.Api.Controllers;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace Lilia.Api.Security;

/// <summary>
/// Leaves the test and developer controllers out of a Production API altogether.
///
/// <para><see cref="E2EController"/> has five anonymous write routes (runs, results,
/// finalize, coverage-events, block-types) whose own header promised an IP allowlist
/// that was never written, and <see cref="DevToolsController"/> mints Stytch
/// magic links, checking "development" inside the handler. Neither belongs on a
/// public API. Not registered means the routes answer 404, nothing to misconfigure.</para>
///
/// <para>A staging API that runs in the Production environment but is the target of CI
/// e2e opts back in with <c>Features:ExposeE2E=true</c> (<c>Features__ExposeE2E</c>).</para>
/// </summary>
public sealed class DevOnlyControllerFeatureProvider : ControllerFeatureProvider
{
    private static readonly HashSet<Type> DevOnly = new() { typeof(E2EController), typeof(DevToolsController) };
    private readonly bool _hide;

    public DevOnlyControllerFeatureProvider(IWebHostEnvironment env, IConfiguration config) =>
        _hide = env.IsProduction() && !config.GetValue<bool>("Features:ExposeE2E");

    protected override bool IsController(TypeInfo typeInfo) =>
        base.IsController(typeInfo) && !(_hide && DevOnly.Contains(typeInfo.AsType()));
}
