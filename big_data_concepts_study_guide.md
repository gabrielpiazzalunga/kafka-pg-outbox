# Big Data & Clearing Architecture: A Study Guide for Senior Engineers

This document compiles architectural explanations for the Iceberg, Spark, and Data Lake implementation used in the Outbound Clearing pipeline.

## 1. Storage Format: Why Parquet?
In a relational database (like Postgres), data is stored in **Rows**. 
If you have a row: `[ID: 1, Name: John, Amount: 100, Payload: {large_json}]`
It is written contiguously on the hard drive. If you run `SELECT sum(amount) FROM table`, Postgres must load the *entire row* (including the large JSON payload) from disk into RAM just to read the `amount` column and jump to the next row. This causes massive Disk I/O.

**Parquet is a Columnar format.**
It pivots the data. It stores 1 million IDs together, then 1 million Names together, then 1 million Amounts together.
If you run `SELECT sum(amount) FROM table` in Spark, the engine looks at the Parquet file's internal metadata and physically only reads the block of disk containing the "Amounts". It skips loading the heavy `payload` block entirely into RAM. 

This means scanning 10 million operations to calculate a clearing total takes megabytes of I/O instead of gigabytes, making it incredibly fast.

## 2. Ingestion: Iceberg & Metadata Mechanics
In a traditional relational DB, saving 10M rows means updating B-Trees, managing WALs, and handling locks. In the Data Lake world, we write files.

*   **Micro-batches:** Your Spark Ingestion job runs every 15 minutes. It pulls JSON messages from Kafka (Debezium CDC).
*   **Writing Parquet:** Spark converts those JSON records into Parquet and writes them to MinIO (`s3://.../network=visa/clearing_date=2026-04-07/file1.parquet`).
*   **The Postgres Catalog:** When Spark finishes writing to S3, Iceberg connects to your Postgres `iceberg_catalog` database and updates a single row: `"Table captured_transactions now points to Snapshot v1"`. 

The Snapshot points to Manifest files in S3. The manifests track exactly what data is in what file (e.g., "File A has `captured_at` from 10:00 to 10:15"). 

**Why this matters:** When your Clearing Job runs, it queries Postgres *once*. Then it reads the Iceberg Manifests in S3 to perform **Partition Pruning**. It skips reading 980 out of 1000 files because the metadata tells it they don't contain the requested timestamps or networks.

## 3. Spark: The Distributed Engine
Spark is not just a Python script; it is a distributed compute cluster.

1.  **The Driver:** When the .NET Orchestrator runs `spark-submit clearing_job.py`, it spins up a single Kubernetes Pod called the Spark Driver.
2.  **The DAG:** `df.filter().repartition().write` builds a Directed Acyclic Graph (an execution plan). It doesn't run until an action like `.count()` or collect is called.
3.  **The Executors:** Spark talks to Kubernetes: *"I need 50 Pods to do this work."* K8s spins up 50 Executor Pods.
4.  **Distributed Execution:** Executor 1 reads `file1.parquet` from S3. Executor 2 reads `file2...` They process data in parallel across 50 machines.

## 4. The Clearing: Reading Iceberg and Chunks
For a 10M operation pipeline:
1.  **Timestamp Bounding:** You pass `PREVIOUS_CUTOFF` and `CURRENT_CUTOFF`.
2.  **Partitioning (200k chunks):** 10M rows / 200,000 = **50 partitions**. You run `df = df.repartition(50)`. Spark shuffles data over the network so 50 Executors hold exactly 200k rows each.
3.  **The MapReduce:** Each Executor maps its 200k rows to fixed-width strings and uses the S3 AWS SDK to `UploadPart` to the Multipart Upload.
4.  **The Trailer:** The Driver calculates the final Visa Header/Trailer from the local sums returned by the Executors and tells S3 to `CompleteMultipartUpload`.

---

## 5. Q&A & Conceptual Deep Dives

### Q1: Cross-Partition Reads (The 8 PM Cutoff Scenario)
> *"If our clearing day ends at 8 PM, we need to perform a count from our previous cutoff which may be in 2 different partitions (yesterday 8 PM to today 8 PM). Because it's partitioned by date, correct?"*

**Exactly right.** 
In Iceberg, the physical S3 folders look like:
- `/clearing_date=2026-04-06/`
- `/clearing_date=2026-04-07/`

If you query `WHERE captured_at > '2026-04-06 20:00:00' AND captured_at <= '2026-04-07 20:00:00'`, Iceberg's query engine automatically identifies that the bounding box covers two logical partitions. 
The Driver will assign some Executors to read the end of yesterday's Parquet files, and other Executors to read today's Parquet files. They all get seamlessly combined into your DataFrame under the hood. You don't have to write any code to handle crossing the midnight boundary.

### Q2: Idempotency in S3 Multipart Uploads
> *"In `clearing_job.py`, the `upload_part` needs to be idempotent. I'm considering the upload_part is either fully success or fully error. Am I right?"*

**Yes, S3 handles the exact idempotency you need here!**
In an S3 Multipart Upload, each chunk is uploaded with a `PartNumber` (e.g., Part 1, Part 2, ... Part 50). 
If a Spark Executor crashes midway through uploading Part 24, Spark will instantly spin up a new Executor and tell it to re-run the exact same block of data for Part 24.
When the new Executor successfully uploads Part 24, S3 will safely overwrite the corrupted/partial Part 24 from the crashed Executor. 
The S3 `upload_part` API guarantees atomicity at the part level. It’s impossible to get "half a part" in the final file. Spark's Task Retries paired with S3 Part Numbers create a perfectly idempotent system.

### Q3: Data Lake Immutability (Merge-On-Read vs Copy-On-Write)
> *"Please elaborate more on Data Lake semantics and immutability."*

In Postgres, if you run `UPDATE journal_entries SET status = 'cleared' WHERE id = 10`, Postgres physically overwrites that bit on the hard drive (or adds a new tuple and marks the old one dead via MVCC).
Data Lakes sit on S3, and **S3 files are immutable**. You cannot edit a Parquet file. 

So, how does Iceberg handle an `UPDATE` or `DELETE` statement if it can't change the file?

**Approach 1: Copy-On-Write (COW - Iceberg Default for Batch)**
If `file1.parquet` has 500,000 rows, and you update *one* of them:
1. Iceberg downloads the whole file into Spark memory.
2. It changes the one row.
3. It writes a brand new `file2.parquet` to S3.
4. It updates the Manifest metadata: "From now on, target queries to `file2.parquet`, and ignore `file1.parquet`". 
*(This is computationally expensive if you do thousands of small updates, which is why we strictly bounded our design by Timestamps instead of `UPDATE clearing_run_id_`!)*

**Approach 2: Merge-On-Read (MOR - Advanced Streaming)**
Instead of rewriting the 500,000 row Parquet file, Iceberg writes a tiny "Delete File" to S3 that says: "Row ID 10 in file1.parquet is deleted/updated."
When you query the data, the engine reads the large Parquet file, reads the tiny Delete file, and applies the differences *in RAM at query time*. 

**The Immutability Superpower: Time Travel**
Because files are never actually overwritten, and only the metadata pointer changes, Iceberg allows you to query the state of the database *in the past*.
You can run:
Iceberg just looks at the older metadata snapshot and reads the older Parquet files that haven't been garbage collected yet. This is incredibly powerful for financial auditing.

---

### Q4: How do Deletes work when running queries in Parquet?
> *"On Parquet, what happens with delete operations, for example if I do a `select sum(amount) from table` or want a specific filter?"*

Because Parquet files themselves are physically immutable, deleting a row doesn't just erase the data from the disk block. Here is exactly what happens when you run `SELECT sum(amount)` on a table that has deletes (using the **Merge-On-Read** strategy):

**1. The Mechanics of a Delete (Merge-On-Read)**
When you issue `DELETE FROM captured_transactions WHERE id = 10`, Iceberg does **not** touch `file1.parquet`. Instead, it creates a very small new file called a **Delete File**.

There are two types of Delete Files:
*   **Position Deletes:** Says *"Ignore Row Line 405 inside `file1.parquet`"*.
*   **Equality Deletes:** Says *"Ignore any row in any file where `id = 10`"*.

**2. Query Execution (`SELECT sum(amount)`)**
When Spark executes your sum aggregation, it builds an execution plan that includes the deletes context:
1.  **Read the Data:** Spark executors load the `amount` column from the large `file1.parquet` into RAM.
2.  **Read the Deletes:** Spark simultaneously loads the small Delete Files into RAM (they are easily broadcast to all nodes because they are tiny).
3.  **The Anti-Join (Merging in RAM):** Spark performs an "Anti-Join" in memory. It scans the `amount` data, but specifically masks or drops the values corresponding to the deleted rows.
4.  **Compute:** It sums the remaining valid amounts and returns the total.

**What this means for performance:**
If you execute a targeted filter like `WHERE network = 'visa'`, Iceberg uses the file Manifests to prune irrelevant Parquet files. However, if those files have associated Delete Files, Spark *must* read the Delete Files to ensure it isn't returning a "ghost" row that was deleted yesterday. 

If a table accumulates millions of Delete Files over time, building that Anti-Join in RAM drastically slows down your `SELECT sum()` queries.

**The Solution: Compaction (The Vacuum Cleaner)**
To prevent query performance from degrading due to too many deletes, Data Engineering teams run periodic maintenance jobs (usually daily or weekly) called **Compaction** (or `RewriteDataFiles`). 
This background job reads the old Parquet files, physically drops the deleted rows, and writes brand new, clean Parquet files. It then updates the Catalog, permanently throwing away the Delete Files. 

So, in the Data Lake world, Deletes are just "instructions for the reader to ignore data" until a maintenance job physically permanently restructures the files.
