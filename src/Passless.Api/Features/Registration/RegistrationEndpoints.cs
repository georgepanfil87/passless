using Fido2NetLib;

namespace Passless.Api.Features.Registration;

internal static class RegistrationEndpoints
{
    public static IEndpointRouteBuilder MapRegistration(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/register").WithTags("Registration");

        group.MapPost("/options", async (
            BeginRegistrationRequest request,
            RegistrationService service,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.DisplayName))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["request"] = ["Username and displayName are required."],
                });
            }

            var options = await service.BeginAsync(request, http, cancellationToken);
            return Results.Ok(options);
        });

        group.MapPost("/verify", async (
            AuthenticatorAttestationRawResponse attestation,
            RegistrationService service,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var outcome = await service.CompleteAsync(attestation, http, cancellationToken);

            // One body for every failure. The caller learns that registration did
            // not happen and nothing else: whether the credential already belongs
            // to somebody, whether the username exists, and whether the challenge
            // was replayed or merely stale are all questions this response
            // refuses to answer. The reason is in the audit log.
            return outcome.Succeeded
                ? Results.Ok(new CompleteRegistrationResponse(outcome.CredentialId))
                : Results.BadRequest(new { error = "registration_failed" });
        });

        return app;
    }
}
