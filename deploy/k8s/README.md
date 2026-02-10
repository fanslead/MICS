# MICS Gateway on Kubernetes (examples)

These manifests are **examples** to help validate `docs/MICS（极简IM通讯服务）需求文档V1.0.md` deployment requirements.

## Apply (minimal)

```bash
kubectl apply -f deploy/k8s/gateway.configmap.yaml
kubectl apply -f deploy/k8s/gateway.headless.service.yaml
kubectl apply -f deploy/k8s/gateway.service.yaml
kubectl apply -f deploy/k8s/gateway.pdb.yaml
kubectl apply -f deploy/k8s/gateway.deployment.yaml
kubectl apply -f deploy/k8s/gateway.hpa.yaml
```

## One-click apply/delete

```bash
# apply base resources (+HPA by default)
deploy/k8s/scripts/apply.sh --namespace mics --create-namespace

# delete base resources
deploy/k8s/scripts/delete.sh --namespace mics
```

## Ingress (WebSocket)

```bash
kubectl apply -f deploy/k8s/gateway.ingress.yaml
```

Notes:
- This ingress only routes WebSocket `/ws` to the `mics-gateway` Service (load-balanced).
- Inter-node gRPC forwarding uses `PUBLIC_ENDPOINT` stored in Redis and should point to the **specific pod**
  via `mics-gateway-headless` DNS (`http://<podname>.mics-gateway-headless:8080`).

## HPA

```bash
kubectl apply -f deploy/k8s/gateway.hpa.yaml
```

Notes:
- CPU-based HPA works out-of-the-box.
- Connections-based HPA requires a Prometheus Adapter exposing `mics_ws_connections` as a Pods custom metric.
