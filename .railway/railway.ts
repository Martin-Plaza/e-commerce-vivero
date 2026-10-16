import { defineRailway, github, project, service } from "railway/iac";
// This repository manages only its own resources in the environment. Other
// repositories export their own partial name.
// See https://docs.railway.com/infrastructure-as-code#multi-repo-projects
export const partial = "api";
export default defineRailway(() => {
  const api = service("api", {
    // dockerfilePath from CaC: "Dockerfile"
    // builder from CaC: "DOCKERFILE"
    source: github("Martin-Plaza/e-commerce-vivero", { branch: "main", checkSuites: true }),
    healthcheck: "/health",
    healthcheckTimeout: 300,
    preDeploy: "dotnet GymShop.Api.dll --initialize-only",
    replicas: { "us-east4-eqdc4a": 1 },
  });

  return project("e-commerce-vivero", {
    variables: { managed: false },
    resources: [api],
  });
});
