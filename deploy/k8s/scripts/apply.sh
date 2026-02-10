#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

NAMESPACE="default"
CREATE_NS="false"
WITH_INGRESS="false"
WITH_HPA="true"
WITH_MONITORING="false"

usage() {
  cat <<EOF
Usage: $(basename "$0") [options]

Options:
  --namespace <ns>        Target namespace (default: ${NAMESPACE})
  --create-namespace      Create namespace if missing
  --with-ingress          Apply Ingress example (WebSocket /ws)
  --no-hpa                Skip HPA example
  --with-monitoring       Apply Prometheus Operator examples (ServiceMonitor/PrometheusRule)
  -h, --help              Show help

Applies (always):
  - gateway.configmap.yaml
  - gateway.headless.service.yaml
  - gateway.service.yaml
  - gateway.pdb.yaml
  - gateway.deployment.yaml
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --namespace)
      NAMESPACE="${2:-}"
      shift 2
      ;;
    --create-namespace)
      CREATE_NS="true"
      shift
      ;;
    --with-ingress)
      WITH_INGRESS="true"
      shift
      ;;
    --no-hpa)
      WITH_HPA="false"
      shift
      ;;
    --with-monitoring)
      WITH_MONITORING="true"
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown arg: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

if [[ "${CREATE_NS}" == "true" ]]; then
  kubectl get namespace "${NAMESPACE}" >/dev/null 2>&1 || kubectl create namespace "${NAMESPACE}"
fi

kubectl apply -n "${NAMESPACE}" -f "${ROOT_DIR}/gateway.configmap.yaml"
kubectl apply -n "${NAMESPACE}" -f "${ROOT_DIR}/gateway.headless.service.yaml"
kubectl apply -n "${NAMESPACE}" -f "${ROOT_DIR}/gateway.service.yaml"
kubectl apply -n "${NAMESPACE}" -f "${ROOT_DIR}/gateway.pdb.yaml"
kubectl apply -n "${NAMESPACE}" -f "${ROOT_DIR}/gateway.deployment.yaml"

if [[ "${WITH_HPA}" == "true" ]]; then
  kubectl apply -n "${NAMESPACE}" -f "${ROOT_DIR}/gateway.hpa.yaml"
fi

if [[ "${WITH_INGRESS}" == "true" ]]; then
  kubectl apply -n "${NAMESPACE}" -f "${ROOT_DIR}/gateway.ingress.yaml"
fi

if [[ "${WITH_MONITORING}" == "true" ]]; then
  kubectl apply -n "${NAMESPACE}" -f "${ROOT_DIR}/monitoring/gateway-servicemonitor.yaml"
  kubectl apply -n "${NAMESPACE}" -f "${ROOT_DIR}/monitoring/gateway-prometheusrule.yaml"
fi

echo "Applied MICS Gateway resources to namespace=${NAMESPACE}"
