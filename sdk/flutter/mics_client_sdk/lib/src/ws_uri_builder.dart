Uri buildWsUri(String baseUrl, String tenantId, String token, String deviceId) {
  final uri = Uri.parse(baseUrl);
  final qp = Map<String, String>.from(uri.queryParameters);
  qp['tenantId'] = tenantId;
  qp['token'] = token;
  qp['deviceId'] = deviceId;
  return uri.replace(queryParameters: qp);
}

