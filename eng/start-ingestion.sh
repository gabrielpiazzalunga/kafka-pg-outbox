#!/bin/bash

# --- Ingestion Management Script ---
# This script builds and restarts the Spark Ingestion job.

# 1. Build the updated ingestion image (v26)
echo "Building Spark Ingestion image..."
docker build -f Clearing.Spark/ingestion_job.Dockerfile -t spark-ingestion:v26 .

# 2. Update the Kubernetes manifest to use the new image
echo "Updating manifest to v26..."
sed -i '' 's/image: spark-ingestion:.*/image: spark-ingestion:v26/' eng/manifests-kraft/spark-ingestion.yaml

# 3. Apply the manifest and restart
echo "Deploying to Kubernetes..."
kubectl apply -f eng/manifests-kraft/spark-ingestion.yaml
kubectl rollout restart deployment/spark-ingestion-ledger

# 4. Success message and log monitoring
echo "--------------------------------------------------------"
echo "✅ Ingestion job is starting."
echo "Wait 15s for the pod to initialize..."
echo "To watch logs, run: kubectl logs -f deployment/spark-ingestion-ledger"
echo "--------------------------------------------------------"
