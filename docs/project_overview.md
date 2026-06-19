---
name: project-overview
description: Dollars2 is a personal zero-based budgeting app cloning EveryDollar, with Plaid and SimpleFIN bank sync
metadata:
  type: project
---

Dollars2 is a zero-based budgeting web app modeled after EveryDollar. Every dollar of income is assigned to a budget category until remaining equals $0. Personal use initially but designed for multi-user.

**Tech stack:** React (Tailwind CSS), .NET backend, MSSQL database, self-hosted

**Why:** User wants EveryDollar functionality with self-hosted control and dual bank sync providers (Plaid free tier + SimpleFIN at $1.50/mo).

**How to apply:** All design decisions should reference EveryDollar as the UX baseline. Keep the app simple — no analytics, no debt tracking, no auto-categorization. See [[budget-structure]], [[transaction-handling]], [[accounts]], [[auth-users]], [[ui-layout]], [[out-of-scope]] for detailed specs.
