#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

NAMESPACE="default"
WITH_INGRESS="false"
WITH_HPA="true"
WITH_MONITORING="false"

usage() {
  cat <<EOF
Usage: $(basename "$0") [options]

Options:
  --namespace <ns>        Target namespace (default: ${NAMESPACE})
  --with-ingress          Delete Ingress example
  --no-hpa                Skip deleting HPA example
  --with-monitoring       Delete Prometheus Operator examples (ServiceMonitor/PrometheusRule)
  -h, --help              Show help
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --namespace)
      NAMESPACE="${2:-}"
      shift 2
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

if [[ "${WITH_MONITORING}" == "true" ]]; then
  kubectl delete -n "${NAMESPACE}" -f "${ROOT_DIR}/monitoring/gateway-prometheusrule.yaml" --ignore-not-found
  kubectl delete -n "${NAMESPACE}" -f "${ROOT_DIR}/monitoring/gateway-servicemonitor.yaml" --ignore-not-found
fi

if [[ "${WITH_INGRESS}" == "true" ]]; then
  kubectl delete -n "${NAMESPACE}" -f "${ROOT_DIR}/gateway.ingress.yaml" --ignore-not-found
fi

if [[ "${WITH_HPA}" == "true" ]]; then
  kubectl delete -n "${NAMESPACE}" -f "${ROOT_DIR}/gateway.hpa.yaml" --ignore-not-found
fi

kubectl delete -n "${NAMESPACE}" -f "${ROOT_DIR}/gateway.deployment.yaml" --ignore-not-found
kubectl delete -n "${NAMESPACE}" -f "${ROOT_DIR}/gateway.pdb.yaml" --ignore-not-found
kubectl delete -n "${NAMESPACE}" -f "${ROOT_DIR}/gateway.service.yaml" --ignore-not-found
kubectl delete -n "${NAMESPACE}" -f "${ROOT_DIR}/gateway.headless.service.yaml" --ignore-not-found
kubectl delete -n "${NAMESPACE}" -f "${ROOT_DIR}/gateway.configmap.yaml" --ignore-not-found

echo "Deleted MICS Gateway resources from namespace=${NAMESPACE}"
