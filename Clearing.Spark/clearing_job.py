import os
import boto3
import math
from pyspark.sql import SparkSession
from pyspark.sql.functions import col, sum, count

# --- Input Configuration ---
# We strip and lower to avoid common trailing space/caps errors in K8s manifests
CLEARING_NETWORK = os.getenv("CLEARING_NETWORK", "visa").strip().lower()
PREVIOUS_CUTOFF = os.getenv("PREVIOUS_CUTOFF", "1970-01-01T00:00:00Z").strip()
CURRENT_CUTOFF = os.getenv("CURRENT_CUTOFF", "2099-12-31T23:59:59Z").strip()
CLEARING_DATE = os.getenv("CLEARING_DATE", "2026-03-26").strip()

# --- S3 Output Configuration ---
BUCKET = "clearing-output"
SAFE_CUTOFF = CURRENT_CUTOFF.replace(":", "").replace("-", "")
S3_KEY = f"output/{CLEARING_NETWORK}/{CLEARING_DATE}/{SAFE_CUTOFF}/clearing_data.txt"
# MinIO Client for MPU initiation/completion
S3_ENDPOINT = os.getenv("S3_ENDPOINT", "http://minio:9000")
S3_ACCESS_KEY = os.getenv("S3_ACCESS_KEY", "minioadmin")
S3_SECRET_KEY = os.getenv("S3_SECRET_KEY", "minioadmin")

s3_client = boto3.client("s3", 
                        endpoint_url=S3_ENDPOINT,
                        aws_access_key_id=S3_ACCESS_KEY,
                        aws_secret_access_key=S3_SECRET_KEY)

# --- Spark Session ---
spark = SparkSession.builder.getOrCreate()

# 1. Initiate S3 Multipart Upload
mpu = s3_client.create_multipart_upload(Bucket=BUCKET, Key=S3_KEY)
MPU_ID = mpu["UploadId"]

def upload_part(idx, iterator):
    rows = list(iterator)
    if not rows: return []
    
    s3 = boto3.client("s3", 
                      endpoint_url=S3_ENDPOINT,
                      aws_access_key_id=S3_ACCESS_KEY,
                      aws_secret_access_key=S3_SECRET_KEY)
    
    # Format rows to fixed-width string
    # Columns matches Visa Base II requirement (Legacy entry ID, ARN, and Gross Amount)
    def format_row(r):
        eid = str(r.entry_id or "")
        arn = str(r.arn or "")
        amt = float(r.gross_amount or 0.0)
        return f"{eid:23}{arn:23}{amt:>12.2f}\n"

    content = "".join([format_row(r) for r in rows])
    
    response = s3.upload_part(
        Bucket=BUCKET, Key=S3_KEY,
        PartNumber=idx + 1,
        UploadId=MPU_ID,
        Body=content.encode("utf-8")
    )
    
    return [{"PartNumber": idx + 1, "ETag": response["ETag"]}]

def main():
    print(f"DEBUG: Filtering for Network={CLEARING_NETWORK}, bounds [{PREVIOUS_CUTOFF}, {CURRENT_CUTOFF}]", flush=True)
    
    df_all = spark.table("clearing_lake.captured_transactions")
    total_rows = df_all.count()
    print(f"DEBUG: Total rows in lake: {total_rows}", flush=True)
    
    # Granular debugging to find where the count disappears
    net_count = df_all.filter(col("network") == CLEARING_NETWORK).count()
    print(f"DEBUG: Rows for network={CLEARING_NETWORK}: {net_count}", flush=True)
    
    time_count = df_all.filter((col("event_timestamp") > PREVIOUS_CUTOFF) & (col("event_timestamp") <= CURRENT_CUTOFF)).count()
    print(f"DEBUG: Rows for Time Bounds: {time_count}", flush=True)

    df = df_all.filter(col("network") == CLEARING_NETWORK) \
               .filter(col("event_timestamp") > PREVIOUS_CUTOFF) \
               .filter(col("event_timestamp") <= CURRENT_CUTOFF)
    
    num_records = df.count()
    if num_records == 0:
        print(f"CRITICAL: Final filter resulted in 0 records. Exiting.", flush=True)
        s3_client.abort_multipart_upload(Bucket=BUCKET, Key=S3_KEY, UploadId=MPU_ID)
        return

    # Calculate Totals
    totals = df.select(
        sum("gross_amount").alias("total_amount"),
        count("*").alias("total_count")
    ).collect()[0]

    # Parallel Upload
    num_partitions = max(1, math.ceil(num_records / 200000))
    print(f"DEBUG: Repartitioning {num_records} rows into {num_partitions} S3 parts.", flush=True)

    parts_info = df.repartition(num_partitions) \
        .rdd.mapPartitionsWithIndex(upload_part) \
        .collect()

    s3_client.complete_multipart_upload(
        Bucket=BUCKET, Key=S3_KEY,
        UploadId=MPU_ID,
        MultipartUpload={'Parts': sorted(parts_info, key=lambda x: x['PartNumber'])}
    )
    print(f"SUCCESS: Clearing file generated at s3://{BUCKET}/{S3_KEY}")

if __name__ == "__main__":
    main()
