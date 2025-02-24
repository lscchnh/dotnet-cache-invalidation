# CacheInvalidation

This project is a PoC showing how to propagate L2 cache invalidation between several instances, each having a memory cache. It uses .NET FusionCache library, a high-performance .NET caching library that combines memory and distributed caching with features like fail-safe mode, background auto-refresh, and a built-in backplane for multi-instance synchronization.

## Prerequisites

- .NET 9 sdk
- docker

## Getting started

Pull garnet image

```sh
docker pull ghcr.io/microsoft/garnet:latest
```

Open `CacheInvalidation.sln`, set `CacheInvalidation.AppHost` as startup project and launch `https` launch profile. The Aspire dashboard page should open with :

- 2 instances of .NET WebApi project, each exposing `GET /data` and `DELETE /invalidate` endpoints
- Garnet cache
- Garnet insight (Garnet cache visualizer)
