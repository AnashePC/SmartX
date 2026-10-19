using SmartX.Shared.Models;

namespace SmartX.Api.Endpoints;

public static class DeploymentEndpoints
{
    public static void MapDeploymentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/deployment").WithTags("Deployment");

        // POST a nested DeploymentNode tree (Facility -> Zone -> Sub-Zone -> Device)
        // and get back every recursive validation error, if any.
        group.MapPost("/validate", (DeploymentNode root) =>
        {
            var result = DeploymentValidator.Validate(root);
            return Results.Ok(result);
        });

        // Convenience endpoint returning a worked example tree so the client
        // (and the marker) can see a realistic Facility -> Zone -> Sub-Zone -> Device chain.
        group.MapGet("/sample", () =>
        {
            var sample = new DeploymentNode
            {
                Id = "FAC-A",
                Name = "Facility A",
                NodeType = "Facility",
                Children =
                {
                    new DeploymentNode
                    {
                        Id = "ZONE-1",
                        Name = "Zone 1",
                        NodeType = "Zone",
                        Children =
                        {
                            new DeploymentNode
                            {
                                Id = "SUBZONE-B",
                                Name = "Sub-Zone B",
                                NodeType = "SubZone",
                                Children =
                                {
                                    new DeploymentNode
                                    {
                                        Id = "ESP32-04",
                                        Name = "ESP32-04",
                                        NodeType = "Device"
                                    }
                                }
                            }
                        }
                    }
                }
            };

            return Results.Ok(sample);
        });
    }
}
