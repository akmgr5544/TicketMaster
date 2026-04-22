Project Overview
TicketMaster is a modern, distributed backend system designed for high-concurrency ticket booking and management.
This project serves as a showcase for implementing Clean Architecture, Domain-Driven Design (DDD), and 
Event-Driven Architecture within the latest .NET ecosystem.

Status: 🚧 Active Development
This project is currently in progress. It is being used to demonstrate architectural patterns and technical
proficiency in building scalable, distributed systems.

🚀 Tech Stack
Language: C# 13 / .NET 10 (Preview)
Architecture: Clean Architecture, DDD, CQRS
Communication: RESTful APIs, gRPC, MassTransit
Database: PostgreSQL (Primary), Redis (Caching)
Messaging: Kafka / RabbitMQ
Testing: xUnit, Moq, FluentAssertions


🏗️ Key Architectural Features
CQRS Pattern: Separation of read and write concerns using MediatR to ensure scalability and maintainability.
Domain-Driven Design: Rich domain models with encapsulated logic, ensuring the business core is independent of external frameworks.
Outbox Pattern: Ensuring data consistency between the database and message broker for reliable event-driven communication.
Global Exception Handling: Centralized middleware for consistent API responses and logging.

📦 Core Modules (Implemented)
Identity Microservice: Robust authentication and authorization using JWT and ASP.NET Core Identity.
Messaging & Event-Driven Logic: Partial implementation of event streaming using RabbitMQ and WolverineFx for decoupled service communication.
Domain Core: Shared kernel and base logic utilizing Domain-Driven Design (DDD) principles.

🗺️ Roadmap / Upcoming Features
Full Event Orchestration: Completing the saga patterns and message handlers with WolverineFx.
Automated Testing Suite: Implementing xUnit and Wolverine's testing library to ensure message delivery and domain logic reliability.
Infrastructure as Code: Dockerizing services and providing a docker-compose for local environment setup.
Persistence: Finalizing the integration between Wolverine's transactional outbox and the primary database.
