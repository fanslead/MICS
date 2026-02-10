export function buildWsUrl(baseUrl: string, tenantId: string, token: string, deviceId: string): string {
  const url = new URL(baseUrl);
  url.searchParams.set("tenantId", tenantId ?? "");
  url.searchParams.set("token", token ?? "");
  url.searchParams.set("deviceId", deviceId ?? "");
  return url.toString();
}

