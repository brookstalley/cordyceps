# Boundary Patterns — Cordyceps

<!-- Contract surfaces where components interact. When changes cross these
     boundaries, the builder investigates consumer impact before completing
     the chunk. The Critic verifies investigation occurred. -->

## Contract Surfaces

<!-- For each boundary, describe: where producers live, where consumers live,
     and what the contract looks like (schema, types, message format). -->

### API Endpoints
<!-- Example:
     Producer: src/api/routes/
     Consumer: src/frontend/src/api/
     Contract: Response models in src/api/models/ define the shape.
-->

### Database Schemas
<!-- Example:
     Producer: src/models/ (ORM definitions)
     Consumer: src/services/ (queries), src/api/ (serialization)
     Contract: Model field names and types.
-->

### Inter-Process Communication
<!-- Example:
     Producer: src/worker/publisher.py
     Consumer: src/main/subscriber.py
     Contract: Message format defined in src/shared/messages.py
-->

### Frontend/Backend Type Contracts
<!-- Example:
     Producer: src/api/routes/ (response shapes)
     Consumer: src/frontend/src/types/ (TypeScript interfaces)
     Contract: Frontend types must match backend serialization exactly.
-->

### Configuration Interfaces
<!-- Example:
     Producer: config/defaults.yaml
     Consumer: All services read config at startup.
     Contract: Config schema in src/config/schema.py
-->

## Test Levels

<!-- Which test levels exist and when each should run. -->

| Level | Exists | When to Run | Location |
|-------|--------|-------------|----------|
| Unit | | Every change | |
| Integration | | Changes crossing boundaries | |
| Contract | | API or schema changes | |
| End-to-end | | Before release / major features | |
