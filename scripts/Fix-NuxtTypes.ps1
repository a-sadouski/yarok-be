param([string]$Source = "$PSScriptRoot/../../yarok")
$ErrorActionPreference = 'Stop'
function Edit([string]$relative, [string]$old, [string]$replacement) {
    $file = Join-Path $Source $relative
    $text = [IO.File]::ReadAllText($file)
    if (!$text.Contains($old)) { throw "Expected text missing in $relative" }
    [IO.File]::WriteAllText($file, $text.Replace($old, $replacement))
}
Edit 'app/components/admin/ProductFormModal.vue' 'v-model="formState.categoryId"' ':model-value="formState.categoryId ?? undefined" @update:model-value="formState.categoryId = $event ?? null"'
Edit 'app/components/admin/ProductFormModal.vue' 'v-model="variant.colorId"' ':model-value="variant.colorId ?? undefined" @update:model-value="variant.colorId = $event ?? null"'
Edit 'app/types/product.ts' 'export interface ProductPreviewCardModel {' "export interface ProductPreviewCardModel {`n  categoryLabel: string;"
Edit 'app/pages/admin/products.vue' "title: formState.name || 'Новый товар'," "title: formState.name || 'Новый товар',`n    categoryLabel: getCategoryLabel(formState.categoryId),"
Edit 'app/pages/admin/products.vue' "firstVariant?.price || ''" "String(firstVariant?.price ?? '')"
Edit 'app/pages/admin/products.vue' "price: variant.price || ''," "price: String(variant.price ?? ''),"
Edit 'app/components/admin/ProductsTable.vue' "accessorKey: 'previewPrice'," "id: 'price',"
Edit 'app/components/admin/ProductsTable.vue' 'formatPrice(row.original.previewPrice)' 'formatPrice(row.original.variants.length ? Math.min(...row.original.variants.map(variant => Number(variant.price))) : 0)'
# Forward the session cookie during server-side route middleware execution.
Edit 'app/middleware/admin.ts' "await `$fetch('/api/admin/me');" "await useRequestFetch()('/api/admin/me');"
Write-Output 'Nuxt admin form types and SSR session forwarding updated.'
