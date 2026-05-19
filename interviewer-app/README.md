# Interviewer App

This project consists of a React (Vite) frontend and a .NET 9 Web API backend to run the internal interviewer platform.

## Prerequisites
- Node.js (v18+)
- .NET 9.0 SDK
- PostgreSQL (running locally on port 5432 with a database named `interviewer`)

---

## 1. Starting the Backend (.NET API)

The backend handles the templates, saves the session notes, and manages the Postgres database migrations automatically.

1. Open a terminal and navigate to the API directory:
   ```bash
   cd Interviewer.Api
   ```
2. Run the application:
   ```bash
   dotnet run
   ```
   *(Note: The first time you run this, it will automatically connect to Postgres, apply the EF Core migrations, and seed the initial "Senior Software Engineer" template.)*

The backend will start and listen on **http://localhost:5243**.

---

## 2. Starting the Frontend (React + Vite)

The frontend is a React application built with Vite and TypeScript.

1. Open a **new** terminal and navigate to the frontend directory:
   ```bash
   cd interviewer-app
   ```
2. Install the dependencies (if you haven't already):
   ```bash
   npm install
   ```
3. Start the Vite development server:
   ```bash
   npm run dev
   ```

The frontend will start and listen on **http://localhost:5173**.

---

## Usage
Once both services are running:
1. Open your browser and navigate to `http://localhost:5173`.
2. Ensure the Template dropdown populates (this confirms the frontend is talking to the backend).
3. Enter a candidate's name and hit **Start Interview**.
4. When finished, hit **End Interview**, fill out the summary, and view the **Results Dashboard**!
