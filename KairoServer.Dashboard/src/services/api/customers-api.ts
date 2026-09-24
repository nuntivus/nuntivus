import { apiGet, apiPost, apiPut, type ApiRequestOptions } from './api-client'
import type { PagedResponse } from './paged-response'

export interface Customer {
  id: string
  name: string
  enabled: boolean
  createdAt: string
  updatedAt: string
}

export interface CustomerListQuery {
  enabled?: boolean
  search?: string
  sortBy?: 'id' | 'name' | 'createdAt' | 'updatedAt'
  sortDirection?: 'asc' | 'desc'
  page: number
  pageSize: number
}

export interface CreateCustomerRequest {
  id: string
  name: string
}

export interface UpdateCustomerRequest {
  name: string
}

export interface ContractListItem {
  id: string
  customerId: string
  enabled: boolean
  eventTypes: string[]
  name?: string | null
  createdAt: string
  updatedAt: string
}

export interface DeliveryListItem {
  id: string
  eventId: string
  customerId: string
  contractId: string
  eventType: string
  status: number | string
  attemptCount: number
  lastError: string | null
  nextAttemptAt: string | null
  createdAt: string
  updatedAt: string
}

export interface FailedDeliveryItem {
  deliveryId: string
  eventId: string
  contractId: string
  customerId: string
  eventType: string
  attemptCount: number
  lastError: string | null
  nextAttemptAt: string | null
  createdAt: string
  updatedAt: string
}

export async function listCustomers(query: CustomerListQuery, options?: ApiRequestOptions) {
  return apiGet<PagedResponse<Customer>>('/v1/customers', {
    enabled: typeof query.enabled === 'boolean' ? query.enabled : null,
    search: query.search?.trim() ? query.search.trim() : null,
    sortBy: query.sortBy ?? null,
    sortDirection: query.sortDirection ?? null,
    page: query.page,
    pageSize: query.pageSize,
  }, options)
}

export async function createCustomer(request: CreateCustomerRequest) {
  return apiPost<Customer>('/v1/customers', request)
}

export async function getCustomerById(id: string, options?: ApiRequestOptions) {
  return apiGet<Customer>(`/v1/customers/${encodeURIComponent(id)}`, undefined, options)
}

export async function updateCustomer(id: string, request: UpdateCustomerRequest) {
  return apiPut<Customer>(`/v1/customers/${encodeURIComponent(id)}`, request)
}

export async function enableCustomer(id: string) {
  return apiPost<Customer>(`/v1/customers/${encodeURIComponent(id)}/enable`)
}

export async function disableCustomer(id: string) {
  return apiPost<Customer>(`/v1/customers/${encodeURIComponent(id)}/disable`)
}

export async function listCustomerContracts(id: string, pageSize = 10, options?: ApiRequestOptions) {
  return apiGet<PagedResponse<ContractListItem>>(
    `/v1/customers/${encodeURIComponent(id)}/contracts`,
    {
      page: 1,
      pageSize,
      sortBy: 'updatedAt',
      sortDirection: 'desc',
    },
    options,
  )
}

export async function listCustomerDeliveries(id: string, pageSize = 10, options?: ApiRequestOptions) {
  return apiGet<PagedResponse<DeliveryListItem>>('/v1/deliveries', {
    customerId: id,
    page: 1,
    pageSize,
    sortBy: 'updatedAt',
    sortDirection: 'desc',
  }, options)
}

export async function listCustomerRecentFailures(id: string, pageSize = 10, options?: ApiRequestOptions) {
  return apiGet<PagedResponse<FailedDeliveryItem>>('/v1/admin/deliveries/failures', {
    customerId: id,
    page: 1,
    pageSize,
    sortBy: 'updatedAt',
    sortDirection: 'desc',
  }, options)
}
