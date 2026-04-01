#!/bin/zsh

# --- Outbound Clearing K8s Hibernate Utility ---

COMMAND=$1

if [[ "$COMMAND" == "pause" ]]; then
    echo "--- Hibernating K8s Cluster (Scaling to 0) ---"
    # Keep the actual disk storage (PVCs) but stop all compute
    kubectl scale deployment --all --replicas=0
    kubectl scale statefulset --all --replicas=0
    echo "--- Cluster Paused. RAM/CPU released. Data is safe on disk. ---"

elif [[ "$COMMAND" == "resume" ]]; then
    echo "--- Resuming Cluster (Scaling to 1) ---"
    # Start infrastructure first (Postgres, Kafka, MinIO)
    kubectl scale statefulset --all --replicas=1
    sleep 5
    # Start applications (Ledger, Ingestion, etc.)
    kubectl scale deployment --all --replicas=1
    echo "--- Cluster Resuming. Check pods with 'kubectl get pods' in 30 seconds. ---"

elif [[ "$COMMAND" == "status" ]]; then
    kubectl get pods
    kubectl get pvc
    FREE_MEM=$(docker info --format '{{.MemTotal}}' 2>/dev/null || echo "Unknown (Check Docker Desktop)")
    echo "Total Docker RAM: $FREE_MEM"

else
    echo "Usage: ./eng/k8s-control.sh [pause|resume|status]"
fi
