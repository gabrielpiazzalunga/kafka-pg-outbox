# Credit Card Acquirer Architecture and Operations

## 1. Introduction and The Role of an Acquirer
A credit card acquirer (or acquiring bank) is a financial institution that enables merchants to accept credit and debit card payments. It acts as the critical intermediary connecting merchants, payment gateways, card networks (like Visa, Mastercard), and issuing banks. 

The acquirer assumes the financial risk of the transaction, processes the authorization, and ensures the merchant receives the funds (settlement). 

## 2. The Transaction Lifecycle
The acquiring process typically involves the following steps:
1. **Initiation**: The customer presents card details at a merchant's point-of-sale (POS) or online payment gateway.
2. **Request Transmission**: The merchant's system securely sends the payment details to the acquirer.
3. **Routing**: The acquirer routes the transaction through the appropriate card network (Scheme) based on the card brand.
4. **Authorization Request**: The card network forwards the request to the customer's Issuing Bank.
5. **Authorization**: The issuing bank verifies available funds, checks for fraud, and approves or declines the transaction.
6. **Response**: The decision flows back through the network to the acquirer, and finally to the merchant.
7. **Clearing & Settlement**: (Usually done in batches at the end of the day). The acquirer receives funds from the issuing bank (via the network) and deposits them into the merchant's account, minus various fees (interchange, network fees, acquirer markup).

## 3. Key Engineering Challenges
Building a state-of-the-art acquirer system involves immense technical complexity:

* **Massive Scale & Throughput:** Systems like Uber's settlement accounting process ~1.2 billion settlements monthly. Acquirers must handle extreme transaction peaks with minimal latency (often under a second for authorization).
* **Data Consistency & Immutability:** Financial systems cannot afford dropped or mutated records. The system must guarantee that every cent is accounted for, requiring strict adherence to immutable ledgers.
* **Integration Complexity:** Acquirers must integrate with dozens of different payment service providers (PSPs), each with entirely different report formats, clearing files, and dispute APIs.
* **Reconciliation and Reporting:** Matching internal transaction records against external bank statements and PSP settlement files is highly error-prone due to timing differences and varying data formats.
* **Security & Compliance:** Strict adherence to PCI-DSS, data encryption, tokenization, and handling Personally Identifiable Information (PII).
* **Chargebacks & Disputes:** Implementing robust workflows to track, manage, and arbitrate reversed transactions and fraud claims.

## 4. High-Level System Architecture
Drawing inspiration from modern systems like Stripe's "Ledger" and Uber's settlement architecture, a modern acquirer should be built around **immutability**, **event-driven processing**, and **double-entry bookkeeping**.

### 4.1. Core Components

#### A. Gateway & Authorization Service (Real-time Flow)
* **API Gateway:** Receives transaction requests from merchants. Handles rate limiting, authentication, and tokenization.
* **Routing Engine:** Determines the optimal path for the transaction (e.g., choosing between multiple acquiring connections if acting as an aggregator, or direct to specific card networks).
* **Risk Engine:** Real-time fraud scoring using ML models before sending to the card network.

#### B. The Core Financial Ledger (System of Record)
* **Immutable Event Log:** Every financial action (charge creation, release, refund, chargeback) is recorded as an immutable event. Traditional CRUD updates are strictly forbidden.
* **Double-Entry Bookkeeping:** Similar to Stripe's Ledger, money movement is modeled as transfers between virtual accounts (e.g., `charge_unsubmitted`, `merchant_balance`, `network_payable`). This ensures the system always mathematically balances to zero. State machines dictate how funds flow between these accounts.

#### C. Settlement & Reconciliation Pipeline (Batch/Asynchronous Flow)
Similar to Uber's three-tier settlement architecture:
* **Feed Ingestion Service:** Connects to Card Networks and internal banking partners to securely download massive end-of-day clearing files and settlement reports.
* **Feed Processor Service:** Normalizes disparate file formats from different networks into a unified canonical data model.
* **Reconciliation Engine:** A highly parallelized service that matches normalized settlement events against the internal Core Ledger. It identifies discrepancies (exceptions, un-cleared funds) for manual or automated review.

#### D. Data Quality (DQ) & Observability Platform
A centralized platform continuously querying the core ledger to ensure system health through three primary metrics:
* **Clearing:** Ensures that transient (intermediate) accounts balance to zero over time. If money is "stuck in the pipes" (e.g., a charge was created but a release event from the network never arrived), it raises an alert.
* **Completeness:** Cross-system checks verifying that every transaction ID in the API Gateway exists in the Ledger and Reconciliation databases.
* **Timeliness:** Measures the delay between event creation and ledger ingestion to ensure SLAs are met for merchant reporting.

### 4.2. Recommended Technology Stack Principles
* **Compute:** Microservices architecture deployed on Kubernetes, allowing independent scaling of the Authorization (compute-heavy) and Ledger (I/O-heavy) services.
* **Data Storage:** 
  * Relational Database (e.g., PostgreSQL/CockroachDB/Aurora) for the Core Ledger to leverage strong ACID guarantees and transactional integrity.
  * Message Brokers (e.g., Kafka) to orchestrate event-driven flows between Authorization, Ledger, and Reconciliation services.
* **Data Warehouse:** Exporting read-replicas of the ledger to a data warehouse (e.g., Snowflake, BigQuery) for generating complex merchant statements, analytics, and accounting reports without impacting transactional performance.

## 5. Architectural Diagrams

### 5.1. High-Level Block Diagram
This shows the core entities involved in a credit card transaction and how the Acquirer sits between the merchant and the payment networks.

```mermaid
graph LR
    Client[Cardholder] -->|Presents Card| Merchant[Merchant / POS]
    Merchant -->|Payment Request| Gateway[Payment Gateway]
    Gateway -->|Routes Transaction| Acquirer[Acquirer]
    Acquirer -->|Auth Request| Network[Card Network]
    Network -->|Auth Request| Issuer[Issuing Bank]
    
    %% Return path
    Issuer -.->|Auth Response| Network
    Network -.->|Auth Response| Acquirer
    Acquirer -.->|Auth Response| Gateway
    Gateway -.->|Auth Response| Merchant
    Merchant -.->|Payment Status| Client
```

### 5.2. Sequence Diagram (Real-Time vs. Asynchronous)
This sequence illustrates which parts of the lifecycle happen in real-time (authorization) versus asynchronously at the end of the day (clearing and settlement).

```mermaid
sequenceDiagram
    autonumber
    actor Client as Cardholder
    participant Merchant as Merchant System
    participant Gateway as Payment Gateway
    participant Acquirer as Acquirer System
    participant Network as Card Network
    participant Issuer as Issuing Bank
    
    Note right of Client: REAL-TIME (Authorization Flow < 1s)
    Client->>Merchant: Starts Checkout & Presents Card
    Merchant->>Gateway: API / Payment Request
    Gateway->>Acquirer: Routes Secure Transaction
    Acquirer->>Acquirer: Risk Check & Pre-auth
    Acquirer->>Network: Authorization Request (Auth)
    Network->>Issuer: Forward Auth Request
    Issuer->>Issuer: Verify funds & fraud checks
    Issuer-->>Network: Approval / Decline Response
    Network-->>Acquirer: Authorization Response
    Acquirer-->>Gateway: Authorization Result
    Gateway-->>Merchant: Payment Status (Success/Fail)
    Merchant-->>Client: Checkout Complete
    
    Note right of Client: ASYNCHRONOUS / BATCH (Clearing & Settlement)
    Note over Merchant, Acquirer: Usually End-of-Day
    Merchant->>Acquirer: Batch Capture (Send authorized txns for clearing)
    Acquirer->>Acquirer: Log to Immutable Ledger (e.g., charge_unsubmitted)
    Acquirer->>Network: Send Clearing File (Demand for funds)
    Network->>Issuer: Process Clearing Request
    Note over Issuer,Network: Funds exchanged between banks
    Issuer-->>Network: Transfer Funds
    Network-->>Acquirer: Deposit Funds (Settlement)
    Acquirer->>Acquirer: Ingest Settlement File & Reconcile vs Ledger
    Acquirer->>Acquirer: Update Ledger State (funds_received)
    Acquirer-->>Merchant: Merchant Payout (Deposit funds minus fees)
```

### 5.3. Clarifications on Sequence Steps

Based on the sequence diagram above, here are detailed explanations addressing common points of confusion:

**Step 13: The "Batch Capture" and the "Merchant System"**
The phrase "Merchant System" can represent different things depending on whether the transaction is online or in-store:
* **Scenario A: E-commerce (Online).** The "Merchant System" is the website's backend server (e.g., Shopify, or a custom web app). In step 13, this backend server sends an API call (a "capture" request) to the Acquirer saying, *"Hey, I successfully shipped the shoes to the customer. You can now finalize the $100 charge we authorized earlier today."*
* **Scenario B: Physical Store (POS Machine).** If you build your own card payment machines (terminals), the POS terminal *itself* acts as the edge of the Merchant System. However, individual terminals rarely talk directly to the Acquirer to send end-of-day batches. Typically, the terminal talks to a central **Terminal Management System (TMS)** or a central merchant server. In step 13, it is either the POS terminal (via a daily automated close-out process) or the central TMS that bundles all the day's authorized transactions and sends the clearing file to the Acquirer.

**Why doesn't it clear immediately at step 12?**
When a card is swiped (Steps 1-12), it is only an **Authorization**. The bank puts a "hold" on the funds, but no actual money moves. A merchant might never "capture" those funds (e.g., a hotel holding funds for incidentals, but releasing them if the mini-bar wasn't used; or an e-commerce store that cancels an order before shipping). Step 13 is the explicit command to turn that "hold" into a real charge.

**Step 16 to 17: How long does it wait? (The Settlement Delay)**
The wait time between Step 16 (Network tells Issuer to transfer funds) and Step 17 (Network deposits funds to Acquirer) is typically **1 to 3 Business Days (T+1 to T+3)**.
* **Why the delay?** The traditional banking infrastructure (like ACH in the US, or various RTGS systems globally) operates in daily batches. When the Card Network calculates that Issuer Bank A owes Acquirer Bank B $10,000 for the day's transactions, Bank A actually wires that money to Bank B through the central banking system. This wire/transfer process takes time to clear. 
* **The Exception:** Some modern systems are moving toward "Next day" or even "Same day" settlement (using systems like FedNow or RTP), but the global standard often still incurs a 24-48 hour delay, especially across borders or on weekends. Because of this delay, the Acquirer must maintain strict ledger states (e.g., marking funds as `charge_captured` vs. `funds_received_from_network`) to avoid paying the merchant before the money has actually arrived at the Acquirer's bank account.

## 6. Brazilian Market Specifics

The architecture and behavior of an acquirer in Brazil are significantly different from the standard US or European models. The Central Bank of Brazil (Bacen) enforces strict regulations, and the market is driven by unique consumer behaviors. Any acquirer operating in Brazil must build their core ledger and settlement engines to handle the following complexities:

### 6.1. Installments ("Parcelado") and Extended Settlements
In the US/EU, if a customer buys a $120 item, the acquirer settles $120 (minus fees) to the merchant within T+2 days.
In Brazil, up to 80% of e-commerce transactions use interest-free installments (*Parcelado sem juros*).
* If a customer buys a $120 item in 12 installments, the cardholder pays their issuer $10 a month.
* **The Acquirer's Challenge:** The standard settlement rule in Brazil for credit cards is **T+30 days** for the *first* installment, T+60 for the second, and so on. 
* **Ledger Impact:** The Acquirer's ledger cannot just log a single `funds_due` event. It must explode a single $120 transaction into 12 distinct future settlement events (receivables) spanning an entire year. The reconciliation engine must track each fraction of the payment arriving individually each month.

### 6.2. Receivables Anticipation (Antecipação de Recebíveis)
Because merchants don't want to wait a year to receive their $120, acquirers in Brazil offer "Anticipation." The acquirer pays the merchant the full amount upfront (e.g., at T+1 or T+2), taking on the cash flow burden and charging a financing fee (discount rate).
* **Architecture Impact:** The Acquirer essentially becomes a lender. The system needs a **Credit/Financing Engine** intertwined with the ledger. When a merchant requests anticipation, the system must calculate the Net Present Value (NPV) of those future 12 installments, deduct the anticipation fee, immediately transfer the funds, and then re-assign those future T+30/60/90 receivables from the "merchant payout" bucket to the "acquirer revenue" bucket to pay back the internal loan.

### 6.3. The Registrars of Receivables (Registradoras)
In 2021, the Brazilian Central Bank mandated that every credit card transaction (receivable) must be legally registered in a centralized external database (Registradoras like CIP, CERC, Tag, Nuclea).
* **The Goal:** To allow merchants to use their future credit card sales as collateral to get loans from *any* bank (not just their acquirer).
* **Architecture Impact:** Acquirers must build a robust **Regulatory Integration Layer**. The moment a transaction is captured, the acquirer has a legal obligation to send an event via heavy API/SFTP integrations to the external Registradoras. 
* If a merchant takes a loan from Bank X using their future receivables, the external Registrar sends an instruction back to the Acquirer: *"Do not pay the merchant their T+30 installment on Friday. Reroute that money to Bank X instead."* The Acquirer's settlement engine must dynamically query these external registries immediately before execution to ensure funds are routed to the correct legal owner (the concept of *Trava de Domicílio*).

### 6.4. Strategic Opportunity: The Bank-Acquirer Hybrid
Because you are building an acquirer *inside* an existing bank, there is a massive competitive advantage in the anticipation and registry space:
* **Internal Anticipation:** Usually, if an acquirer wants to prepay a merchant (anticipation), they borrow money from a bank at a certain interest rate, add their own margin, and offer the anticipation to the merchant. Since you *are* the bank, your cost of capital is much lower. You can offer anticipation at very competitive rates and capture the entire spread.
* **Registry Synergies:** While you legally *must* register receivables with external registradoras (like CIP/Nuclea), your bank's lending arm can aggressively target your own acquiring merchants. Because you already have the pristine ledger data of the merchant's transactions, your internal credit risk models can pre-approve them for loans almost instantly, outcompeting external banks who have to rely solely on slower registry queries.


## 7. Deep Dive: Inside the Acquirer System

The external diagrams show the flow, but internally, the Acquirer must handle tremendous edge cases and complexity.

### 7.1. Internal Services Architecture (Event-Driven)
A modern acquirer is heavily event-driven (using Apache Kafka, AWS Kinesis, etc.). Here are the core internal services:
1. **Gateway / Auth Service:** Receives the original JSON or ISO-8583 requests from terminals. Validates the merchant credentials, unpackages the request, and securely passes PIN blocks to the HSM.
2. **Switch / Routing Engine:** Taking the validated request, it decides where to route the transaction (e.g., to Visa, Mastercard, or an internal core banking system for "On-Us" transactions).
3. **Ledger Service:** The immutable database where all financial state changes are recorded in a double-entry format. An initial authorization creates a pending state here.
4. **Clearing File Generator (Outbound File Service):** Batch process that runs at the end of the day to build the demand files.
5. **Feed Ingestion & Reconciliation Service:** Reads the inbound settlement files from the network and matches them against the internal ledger.
6. **Balance & Payout Engine:** A service that continuously processes the ledger (via materialized views or streaming aggregation) to calculate the merchant's available balance and trigger bank payouts.

#### Internal Architecture Diagram

```mermaid
graph TD
    subgraph Merchant Layer
        POS[POS Terminal]
        Ecom[E-commerce Backend]
    end

    subgraph Internal Acquirer System
        Auth[Auth & Tokenization Service]
        HSM[[Hardware Security Module]]
        Switch[Switch / Routing Engine]
        Kafka[(Event Bus / Kafka)]
        Ledger[(Core Ledger DB)]
        Outbound[Clearing File Generator]
        Inbound[Feed Ingestion Service]
        Recon[Reconciliation Engine]
        Balance[Balance Service]
        Exception[Exception Dashboard]
    end

    subgraph Card Networks
        Visa[Card Network]
    end

    %% Real-time Flow
    POS -->|1. Auth Request| Auth
    Ecom -->|1. Auth Request| Auth
    Auth <-->|2. Validate PIN Block| HSM
    Auth -->|3. Validated Request| Switch
    Switch -->|4. Route ISO-8583| Visa
    Switch -->|5. Publish Auth Event| Kafka
    Kafka -->|6. Persist state: charge_authorized| Ledger

    %% End of day outbound batch
    POS -->|7. Capture Request| Switch
    Switch -->|8. Publish Capture Event| Kafka
    Kafka -->|9. Persist state: charge_captured| Ledger
    Ledger -.->|10. CDC / Scheduled Batch Read| Outbound
    Outbound -->|11. Send Clearing File| Visa

    %% End of day inbound settlement
    Visa -.->|12. Drop Settlement File| Inbound
    Inbound -->|13. Publish Settlement Events| Kafka
    Kafka -->|14. Consume Events| Recon
    Ledger -.->|15. Read Expected States| Recon
    Recon -->|16. Update state: funds_received| Ledger
    Recon -.->|Mismatch / Orphan| Exception
    Ledger -.->|17. Continuous Aggregation| Balance
```

### 7.2. Creating the Clearing File and Ledger States
When the merchant says "capture these 5,000 transactions," the Acquirer must build a "Clearing File" to send to Visa/Mastercard. 
* **The Catch in Building the File:** 
    * Format: Networks require very specific, legacy tape-based formats (like Visa's Base II or Mastercard's IPM). A single misplaced byte ruins the file.
    * Batching rules: A merchant might send 10 small capture requests. The acquirer must aggregate these correctly.
    * Timing: Networks have strict daily cut-off times. If you miss the 4:00 PM EST window, you don't get paid until the *next* day, severely disrupting cash flow.
* **Dealing with Ledger States:**
    * **State 1 (`charge_authorized`):** Immediately after swiping. No money moves.
    * **State 2 (`charge_captured`):** Merchant captured it. The Acquirer's Ledger Service creates an event moving funds from `charge_authorized` to `charge_captured`. 
    * **State 3 (`clearing_sent`):** The Outbound File Service generates the file for Visa and updates the ledger. Now the Acquirer knows, *"I have demanded this money from the network."*

### 7.3. Feed Ingestion: Reading Files & Breaking into Events
Yes, the exact approach when receiving the massive end-of-day settlement files from the Networks is to stream the file and break it down into thousands (or millions) of individual events.
1. The Network drops a multi-gigabyte flat file onto a secure SFTP server.
2. The **Feed Ingestion Service** picks it up, parses the legacy format, and publishes a standard internal event (e.g., `SettlementEventReceived`) to a Kafka topic for *every single line* in the file.
3. This massive fan-out allows dozens of consumer pods to process the reconciliation in parallel, dramatically speeding up end-of-day accounting.

### 7.4. The Reconciliation Engine: What can go wrong?
Reconciliation is comparing the internal Ledger (`clearing_sent`) against the Network's file (`settlement_received`) to verify every penny matches. 

**Common Scenarios and Failures:**
* **The Perfect Match (1-to-1):** Internal Ledger says Transaction X is $100. Network file says they settled Transaction X for $100. The engine moves the ledger state to `funds_received`.
* **Scenario A: The Missing Settlement (Orphaned Record):** We demanded $100 for Transaction Y yesterday. The Network file arrived today, but Y isn't in there. 
    * *Action:* The engine flags Y in an Exception Queue and freezes the merchant payout for Y.
    * *Resolution:* Usually relies on an automated "wait 24 hours" rule, as networks sometimes split files across days. If it's still missing, operations creates a manual investigation ticket with the Network.
* **Scenario B: Amount Mismatch (Tolerance issues):** We demanded $100 for Transaction Z, but the Network settled $99.98.
    * *Cause:* Unexpected cross-border foreign exchange (FX) rate fluctuations, or a late-added scheme fee. 
    * *Action:* If within an acceptable threshold ($0.02), an automated rule posts a "write-off" event to the ledger to balance it to zero and proceeds. If it is way off ($50), it goes to the Exception Queue for manual review.
* **Scenario C: Unexpected Network Settlement (Recon Orphan):** The network file says they are paying us $50 for Transaction W, but our internal Ledger has absolutely no record of Transaction W ever occurring.
    * *Cause:* A severe routing error, a bug in our Auth system failing to log the event, or a file formatting parsing issue.
    * *Action:* The funds are deposited into a generic "Suspense/Holding Account" in the general ledger. They cannot be paid to a merchant because we don't know who owns the money. Operations must investigate to find the true owner.
* **Scenario D: Surprises (Chargebacks & Fines):** The network file contains a -$50 chargeback for a transaction from three months ago that the customer disputed as fraud.
    * *Action:* The engine ingests this unexpected negative event, updates the merchant's balance to deduct the $50, and triggers the Dispute Service to notify the merchant to submit evidence to fight it.

### 7.5. Ledger State Machine
As discussed, events flowing through Kafka aren't just logs; they are **state transitions** applied to the core ledger. To answer your question on who processes balances: A **Balance Service** continuously aggregates these ledger states (often using CDC—Change Data Capture—like Debezium, or materialized views) to show the merchant their "Pending" vs. "Available" balance.

Here is a simplified state machine for a single transaction's lifecycle in the ledger:

```mermaid
stateDiagram-v2
    [*] --> charge_authorized : 1. Initial Swipe (Funds placed on Hold)
    charge_authorized --> charge_captured : 2. Merchant Captures (End-of-day)
    charge_authorized --> charge_voided : 2b. Merchant Cancels (Order aborted)
    charge_authorized --> auth_expired : 2c. Auth Expires (No capture received)
    charge_captured --> clearing_sent : 3. Acquirer demands funds from Network
    clearing_sent --> funds_received : 4. Network settles funds to Acquirer
    funds_received --> pending_merchant_payout : 5. Queued for Merchant (T+1/T+30 wait)
    pending_merchant_payout --> payout_completed : 6. Money wired to Merchant Bank Account
    
    %% Exception Flows
    funds_received --> chargeback_deducted : Customer Disputes Charge
```

**What happens when a capture never arrives for an authorized transaction?**
This is the `auth_expired` state in the diagram above. Authorizations have a limited lifespan (typically 7-30 days, depending on the card network and merchant category). If the merchant never sends a capture request (e.g., the customer cancelled the order, or the merchant's system had a bug), the hold on the cardholder's funds simply expires. 
* **On the Acquirer side:** A scheduled job must sweep the Ledger for any transactions stuck in `charge_authorized` past their expiry window. It then posts a reversal event (`auth_expired`), which credits back `auth_holding` to zero. No money ever moved, so there is no financial risk—but the Acquirer *must* clean up its ledger to avoid phantom balances inflating reports.
* **On the Cardholder side:** The hold disappears from their card statement, and their available credit is restored.

### 7.6. The Double-Entry Ledger Example

#### What is Double-Entry Bookkeeping?
The core rule is simple: **every financial event must touch exactly two accounts, and the total of all Debits must always equal the total of all Credits across the entire system.** Think of it like energy conservation in physics—money is never created or destroyed, only transferred. 
* **Debit (Dr):** Increases the balance of that account (money flows *into* it).
* **Credit (Cr):** Decreases the balance of that account (money flows *out* of it).

This guarantees that if you sum every single Debit ever recorded and subtract every single Credit ever recorded across all accounts, you always get exactly **$0.00**. If you don't get zero, something is broken. This is the core integrity check of any financial system.

#### Step-by-Step for a $100 Transaction
Consider a customer buying a **$100 physical product**. *(This follows the Brazilian T+30 timeline assuming NO anticipation.)*

| Time | Event Action | Account | Debit (Dr) | Credit (Cr) | Explanation |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Day 1: 10:00** | `swipe_auth` | `auth_holding` | $100 | | Money-in: We record a $100 hold. |
| | | `external_customer_card` | | $100 | Money-out: The cardholder's available limit decreases. |
| **Day 1: 23:00** | `capture_batch` | `charge_captured` | $100 | | Money-in: The confirmed charge account receives $100. |
| | | `auth_holding` | | $100 | Money-out: The temporary hold is emptied. |
| **Day 2: 02:00** | `registry_sync` | *[N/A — Regulatory]* | — | — | *BRAZIL:* Register the receivable with CIP/Tag. No money moves. |
| **Day 2: 23:30** | `clearing_sent` | `network_receivable` | $100 | | Money-in: We now expect $100 from Visa. |
| | | `charge_captured` | | $100 | Money-out: The captured charge is emptied. |
| **Day 3: 08:00** | `settlement_recon` | `acquirer_bank_account` | $98 | | Money-in: Visa wires $98 physical cash into our bank. |
| | | `network_receivable` | | $98 | Money-out: $98 of the $100 we expected has arrived. |
| **Day 3: 08:00** | `fee_reconciliation` | `fee_expense_account` | $2 | | Money-in: We record a $2 business expense. |
| | | `network_receivable` | | $2 | Money-out: The remaining $2 clears `network_receivable` to $0. |
| **Day 30: 09:00** | `merchant_payout` | `merchant_payable` | $97 | | Money-in: We recognize a $97 liability to the merchant. |
| | | `acquirer_bank_account` | | $97 | Money-out: We earmark $97 from our bank for payout. |
| **Day 30: 15:00** | `wire_transfer` | `merchant_external_bank` | $97 | | Money-in: The merchant's external bank receives $97. |
| | | `merchant_payable` | | $97 | Money-out: Our liability to the merchant clears to $0. |

> **Integrity Check:** Sum all Debits = $100+$100+$100+$98+$2+$97+$97 = **$594**. Sum all Credits = $100+$100+$100+$98+$2+$97+$97 = **$594**. The system balances perfectly. ✅

#### Account Balances over Time
The running balance of each account after each step (Internal vs External):

| Event | External Balances (Not Controlled) | Internal Balances (Controlled by Acquirer) |
| :--- | :--- | :--- |
| **Initial State** | Customer Card: `$0`<br>Merchant Bank: `$0` | All Internal Accounts: `$0` |
| **After `swipe_auth`** | Customer Card: `-$100` | `auth_holding`: `+$100` |
| **After `capture_batch`** | Customer Card: `-$100` | `charge_captured`: `+$100`<br>`auth_holding`: `$0` |
| **After `clearing_sent`** | Customer Card: `-$100` | `network_receivable`: `+$100`<br>`charge_captured`: `$0` |
| **After `settlement_recon` & `fee`** | Customer Card: `-$100` | `acquirer_bank_account`: `+$98`<br>`fee_expense_account`: `+$2`<br>`network_receivable`: `$0` |
| **After `merchant_payout`** | Customer Card: `-$100` | `acquirer_bank_account`: `+$1`<br>`fee_expense_account`: `+$2`<br>`merchant_payable`: `+$97` |
| **After `wire_transfer`** | Customer Card: `-$100`<br>Merchant Bank: `+$97` | `acquirer_bank_account`: `+$1` <br>`fee_expense_account`: `+$2`<br>`merchant_payable`: `$0` |

*End state: The merchant got $97, the network kept $2, and the acquirer retained $1 profit.*

### 7.7. Bypassing the Network: "On-Us" Transactions
*If I identify the issuing bank is mine, I can skip the network, right?*
**Yes.** This is the holy grail for a Bank-Acquirer hybrid. It is called an **"On-Us" transaction**.
If your POS terminal reads a card and the Switch recognizes the BIN (Bank Identification Number) belongs to *your* bank, the Switch bypasses Visa/Mastercard entirely and routes the authorization directly to your internal core banking issuer system. 
* **The Benefit:** You pay **zero** network scheme fees for these transactions, drastically increasing your profit margin. Settlement is instantaneous because the money simply moves from one internal bank account to another.

### 7.8. Hardware Security Modules (HSMs) and PIN Validation
*How do I confirm PINs? Is there a special way to store and access secrets?*
PINs must NEVER touch a standard database or application server in plaintext. The payment industry relies on **Hardware Security Modules (HSMs)**—highly secure, tamper-resistant physical servers.
* **The Flow:** When a user types their PIN on a POS pad, the pad encrypts it immediately into a "PIN Block" using a secure key it was injected with at the factory.
* **The Acquirer:** The Auth Service receives this encrypted PIN block. *The Auth Service code cannot decrypt it.* 
* **Validation:** 
    * If the transaction is routed to Visa, the acquirer just passes the encrypted block blindly through the Switch. 
    * If it is an **"On-Us"** transaction (as described above), your internal Auth Service sends the encrypted PIN Block strictly to your *internal* HSM cluster via secure socket (often using protocols like Thales Payshield). The HSM has the master keys safely trapped in hardware, decrypts the block entirely inside its secure boundary, compares it to the correct hashed PIN for that card, and returns a simple `YES` or `NO` back to your Auth Service.

### 7.9. Deep Dive: Feed Ingestion Architecture & Challenges
The Feed Ingestion Service is arguably one of the most operationally complex pieces of an acquirer. It's the bridge between the legacy file-based banking world and the modern event-driven microservices world.

Here is a critical architectural breakdown of how it should be built, and what can go disastrously wrong.

#### A. How the Architecture Looks
Modern ingestion pipelines typically follow a "Cloud Storage + Event Trigger + Worker Pool" pattern:

```mermaid
graph TD
    subgraph External Networks
        Visa[Visa / Mastercard]
    end

    subgraph DMZ / Edge
        SFTP[SFTP / Connect:Direct Server]
    end

    subgraph Internal Architecture
        S3[(AWS S3 / Bucket)]
        Topic((SNS / SQS Queue))
        Kafka[(Main Kafka Event Bus)]
        DB[(File Tracking DB)]
        DLQ[(Dead Letter Queue)]
        Redis[(Redis Cache)]
    end

    subgraph Kubernetes Cluster
        Worker[Feed Ingestion Worker Pods]
    end

    %% Data Flow
    Visa -->|1. Drop flat file| SFTP
    SFTP -->|2. Move & Delete| S3
    S3 -->|3. Trigger FileArrivedEvent| Topic
    Topic -->|4. Claim Event| Worker
    Worker <-->|5. Check SHA-256 Hash Idempotency| DB
    Worker -->|6. Insert state: PENDING / PROCESSING| DB
    S3 -.->|7. Stream file in chunks| Worker
    Worker <-->|8. Store running totals for Trailer Math| Redis
    Worker -->|9. Publish row-by-row as JSON| Kafka
    Worker -.->|Parser Error| DLQ
    Worker -->|10. Update state: COMPLETED| DB
```

1. **The Drop Zone:** Visa/Mastercard drops a flat file (e.g., standard `.csv`, or legacy fixed-width `.txt` formats) via a secure tunnel (SFTP/Connect:Direct) into an external-facing DMZ.
2. **Move to Cloud Storage:** A simple script or listener immediately moves that file into a secure cloud object store (like an AWS S3 `inbound/` bucket) and deletes it from the edge server.
3. **Event Generation:** The moment the file lands in the `inbound/` bucket, the S3 bucket fires an event (e.g., via AWS SNS/SQS or a Kafka topic: `FileArrivedEvent`).
4. **The Processing Workers:** A pool of horizontally scaled Feed Ingestion worker pods listens to that event. One pod claims the file, validates the SHA-256 hash against the DB to skip duplicates, streams it line by line (using Redis to count totals), transforms each line into JSON, and pumps it onto the main Kafka `SettlementEvent` topic.

#### B. How do we manage files we already read?
You must maintain a relational **File Tracking Database (e.g., PostgreSQL)**. Never rely solely on moving files between S3 folders (like `inbound/` -> `processed/`) as your source of truth, as S3 is not a transactional database.

A typical `Settlement_Files` table schema includes:
* `network_file_id` (Unique hash or filename from the network)
* `status` (PENDING, PROCESSING, COMPLETED, FAILED)
* `total_records_expected` (From the file's control trailer)
* `records_successfully_processed`
* `created_at` / `completed_at`

**The State Machine Flow:**
1. File lands -> Row inserted as `PENDING`.
2. Worker picks it up -> Updates row to `PROCESSING`.
3. Worker finishes -> Updates row to `COMPLETED` and moves the S3 object to an `archive/` bucket. 

#### D. Critical Architectural Problems (What goes wrong?)

Being critical, here are the nastiest problems an architect must solve for Feed Ingestion:

**1. The "Out of Memory" (OOM) File Bomb**
* **The Problem:** A junior engineer writes a script that says `file_contents = s3.get_object().read()`. Visa drops a 15GB settlement file. The Kubernetes pod hits its 2GB memory limit and crash-loops forever.
* **The Solution:** The ingestion service must **Stream Process**. It must read the file in chunks or stream it line-by-line (`streamReader.readLine()`), process that one line, emit it to Kafka, and immediately garbage collect the memory. 

**2. The Re-delivery Dilemma (Idempotency)**
* **The Problem:** What if the worker pod crashes halfway through processing a 1,000,000 line file? Or what if Visa accidentally drops the exact same file twice on the SFTP?
* **The Solution:** 
    * *File-level Idempotency:* Before processing, hash the file (SHA-256) or check the Network sequence number against the File Tracking DB. If you've seen it, reject it.
    * *Row-level Idempotency:* Kafka consumers downstream (the Reconciliation Engine) must be idempotent. If the Feed Ingester crashes at line 500,000, it simply spins back up and restarts from line 1. The Reconciliation Engine must recognize, *"I've already updated the ledger for Transaction #123,"* and safely ignore the duplicate event.

**3. Partial Failures (The "Bad Apple" Row)**
* **The Problem:** A file has 500,000 rows. Row 400,123 has a malformed amount string containing a letter (`10A.00`). A poorly written parser throws an unhandled Exception, and the entire file fails to process. 499,999 merchants don't get paid because of one bad row.
* **The Solution:** The parser must catch row-specific exceptions. If a row is garbage, the worker writes that specific raw text line to a **Dead Letter Queue (DLQ)**, increments an `error_count` metric, and continues processing the rest of the file. Operations can manually fix the DLQ later.

**4. The Trailer Totals Mismatch**
* **The Problem:** Legacy banking files usually have a "Header" (start date), millions of "Detail" rows, and a "Trailer" at the bottom that says `Total_Records: 1000000, Total_Amount: $50,000,000`. 
* **The Solution:** The Feed Ingester must hold state in **Redis** (or memory) during processing because the File Tracking DB is too slow for millions of rapid increments. As it streams, it increments row counts and sums amounts in Redis. When it hits the Trailer row, it compares its internal math to the Trailer's math. If they do not match, the file is corrupt or truncated. The system must raise a critical alarm and halt downstream payouts.
    * **Parallel Processing & Redis Concurrency (.NET Context):** If you are reading the S3 stream using a single reader thread and fanning out the actual parsing to a pool of parallel worker threads (e.g., using .NET `System.Threading.Channels`), you *must* rely on Redis for the centralized aggregation to avoid race conditions.
    * *The Concurrency Gotcha:* If Thread A and Thread B both try to read the current count from Redis, add $10, and write it back `(Get -> Add -> Set)`, they might overwrite each other. 
    * *The Fix:* You must use Redis's native atomic increment commands. In C# (using StackExchange.Redis), you would use `db.StringIncrementAsync("file_123_count")` and `db.StringIncrementAsync("file_123_amount", 10.50)`. Because Redis is single-threaded at its core, these `INCR` / `INCRBYFLOAT` commands are guaranteed absolutely mathematically correct, no matter how many parallel .NET threads hit them simultaneously.

**5. How to Chunk a 15GB S3 File for Parallel Workers (The .NET Channel Pattern)**
* **The Problem:** You have a 15GB text file and want to limit memory usage to ~10MB while keeping 10 worker threads busy. Do you read 1MB raw byte chunks, or do you read by lines?
* **The Solution (Line Batching):** For text-based financial files (CSV, fixed-width), you must chunk by **lines**, not arbitrary byte sizes. If you blindly read a 1MB byte chunk, your stream will almost certainly slice a 120-character transaction line directly in half (e.g., bytes stop at `"John Doe, 150"` instead of `"John Doe, 150.00"`). The parser worker will catastrophicly fail on the broken string.
* **The Pattern (Producer-Consumer via Channels):**
    1. **The Producer (1 Reader Thread):** A blazing fast single thread opens the continuous S3 `StreamReader`. It calls `.ReadLineAsync()` in a tight loop. Because the reader only holds one string in memory at a time, its memory footprint is effectively zero.
    2. **The Batcher:** Instead of pushing individual lines to a `.NET Channel` (which creates too much locking overhead), the Producer adds lines to a `List<string>`. Once the list hits exactly `10,000` lines (a logical block of work), it creates an immutable array and `WriteAsync`'s that entire array into the `Channel`.
    3. **Bounded Channels (Backpressure):** You create the channel with `BoundedChannelOptions(Capacity = 10)`. This is the most crucial part! If the 10 worker threads get slow, the Channel fills up with 10 arrays (totaling 100,000 lines). The blazing fast Producer thread is *forced to pause* and wait. This enforces **Backpressure**, mathematically guaranteeing your application will never process more than 10 blocks at the same time, keeping RAM strictly capped.
    4. **The Consumers (10 Worker Threads):** Ten parallel threads call `Channel.ReadAsync()`. A worker grabs an array of 10,000 perfect, unbroken string lines, iterates through them, parses the amounts, fires the Kafka events, sends the atomic increments to Redis, and then reaches back into the Channel for the next array.
