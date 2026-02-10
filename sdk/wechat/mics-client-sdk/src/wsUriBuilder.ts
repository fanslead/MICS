export function buildWsUrl(baseUrl: string, tenantId: string, token: string, deviceId: string): string {
  const u = new URL(baseUrl);
  u.searchParams.set("tenantId", tenantId);
  u.searchParams.set("token", token);
  u.searchParams.set("deviceId", deviceId);
  return u.toString();
}

