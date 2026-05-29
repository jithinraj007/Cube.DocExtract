// Dashboard Controller Logic
document.addEventListener('DOMContentLoaded', () => {
    // State management
    let documents = [];
    let selectedIds = new Set();
    let pollingIntervals = {};

    // Elements
    const dropzone = document.getElementById('dropzone');
    const fileInput = document.getElementById('fileInput');
    const docTableBody = document.getElementById('docTableBody');
    const searchInput = document.getElementById('searchInput');
    const bulkActionsContainer = document.getElementById('bulkActions');
    const selectedCountSpan = document.getElementById('selectedCount');
    const queueList = document.getElementById('queueList');
    
    // Drawer Elements
    const drawerBackdrop = document.getElementById('drawerBackdrop');
    const drawer = document.getElementById('drawer');
    const btnCloseDrawer = document.getElementById('btnCloseDrawer');
    const drawerForm = document.getElementById('drawerForm');
    const btnAddLineItem = document.getElementById('btnAddLineItem');
    const lineItemsList = document.getElementById('lineItemsList');
    const rawTextViewer = document.getElementById('rawTextViewer');
    const currentDocIdInput = document.getElementById('currentDocId');
    const paneTitleFile = document.getElementById('paneTitleFile');
    
    // Stats Elements
    const statTotal = document.getElementById('statTotal');
    const statCompleted = document.getElementById('statCompleted');
    const statPending = document.getElementById('statPending');
    const statFailed = document.getElementById('statFailed');

    // Export Forms
    const exportFormatInput = document.getElementById('exportFormat');
    const exportIdsInput = document.getElementById('exportIds');
    const exportForm = document.getElementById('exportForm');

    // Initialize Page
    loadDocuments();

    // 1. Drag & Drop Event Listeners
    dropzone.addEventListener('click', () => fileInput.click());
    
    fileInput.addEventListener('change', (e) => {
        if (e.target.files.length > 0) {
            handleFileUpload(e.target.files);
        }
    });

    dropzone.addEventListener('dragover', (e) => {
        e.preventDefault();
        dropzone.classList.add('dragover');
    });

    ['dragleave', 'dragend'].forEach(type => {
        dropzone.addEventListener(type, () => {
            dropzone.classList.remove('dragover');
        });
    });

    dropzone.addEventListener('drop', (e) => {
        e.preventDefault();
        dropzone.classList.remove('dragover');
        if (e.dataTransfer.files.length > 0) {
            handleFileUpload(e.dataTransfer.files);
        }
    });

    // 2. Fetch Document List
    async function loadDocuments() {
        try {
            const res = await fetch('/api/document/list');
            if (!res.ok) throw new Error('Failed to load documents.');
            
            documents = await res.json();
            renderDocuments();
            updateStats();
            setupPollingForIncompleteDocs();
        } catch (err) {
            console.error(err);
            showNotification('Error loading documents database', 'error');
        }
    }

    // 3. Render Documents in Table
    function renderDocuments() {
        const query = searchInput.value.toLowerCase();
        
        const filteredDocs = documents.filter(doc => {
            const fileName = doc.fileName.toLowerCase();
            const poNum = (doc.metadata?.poNumber || '').toLowerCase();
            const vendor = (doc.metadata?.vendorDetails || '').toLowerCase();
            const status = doc.status.toLowerCase();
            return fileName.includes(query) || poNum.includes(query) || vendor.includes(query) || status.includes(query);
        });

        if (filteredDocs.length === 0) {
            docTableBody.innerHTML = `
                <tr>
                    <td colspan="7">
                        <div class="empty-state">
                            <div class="empty-state-icon">📂</div>
                            <h3>No documents found</h3>
                            <p>Upload a PDF, Image, or Excel file to get started.</p>
                        </div>
                    </td>
                </tr>
            `;
            return;
        }

        docTableBody.innerHTML = filteredDocs.map(doc => {
            const isChecked = selectedIds.has(doc.id) ? 'checked' : '';
            const isSelectedClass = selectedIds.has(doc.id) ? 'selected' : '';

            // Format dates safely
            const uploadDate = new Date(doc.uploadDate).toLocaleString();
            const poDate = doc.metadata?.poDate ? new Date(doc.metadata.poDate).toLocaleDateString() : '—';
            const deliveryDate = doc.metadata?.deliveryDate ? new Date(doc.metadata.deliveryDate).toLocaleDateString() : '—';

            // Extract vendor details (first 30 chars)
            const vendorDetails = doc.metadata?.vendorDetails 
                ? (doc.metadata.vendorDetails.split(',')[0] || '—').substring(0, 30) + (doc.metadata.vendorDetails.length > 30 ? '...' : '')
                : '—';

            // Total amount subtotal
            const totalAmount = doc.lineItems?.reduce((sum, item) => sum + (item.amount || 0), 0) || 0;
            const formattedTotal = totalAmount > 0 ? `$${totalAmount.toFixed(2)}` : '—';

            return `
                <tr class="${isSelectedClass}" data-id="${doc.id}">
                    <td class="checkbox-col" onclick="event.stopPropagation();">
                        <input type="checkbox" class="doc-checkbox" data-id="${doc.id}" ${isChecked}>
                    </td>
                    <td onclick="openDocDrawer(${doc.id})">
                        <div style="font-weight:600;">${escapeHtml(doc.fileName)}</div>
                        <div style="font-size:0.75rem; color:var(--text-secondary);">${uploadDate}</div>
                    </td>
                    <td onclick="openDocDrawer(${doc.id})">
                        <span class="badge ${getFileTypeBadgeClass(doc.fileType)}">${escapeHtml(doc.fileType)}</span>
                    </td>
                    <td onclick="openDocDrawer(${doc.id})">
                        <span class="status-pill status-${doc.status.toLowerCase()}">
                            <span class="status-dot"></span>
                            ${doc.status}
                        </span>
                    </td>
                    <td onclick="openDocDrawer(${doc.id})">
                        <code style="font-weight:600; color:#0066cc;">${escapeHtml(doc.metadata?.poNumber || '—')}</code>
                    </td>
                    <td onclick="openDocDrawer(${doc.id})" title="${escapeHtml(doc.metadata?.vendorDetails || '')}">
                        <span style="font-size:0.85rem;">${escapeHtml(vendorDetails)}</span>
                    </td>
                    <td onclick="openDocDrawer(${doc.id})" style="font-size:0.85rem;">${poDate}</td>
                    <td onclick="openDocDrawer(${doc.id})" style="font-size:0.85rem;">${deliveryDate}</td>
                    <td onclick="openDocDrawer(${doc.id})" style="font-weight:700; color:#10a760;">${formattedTotal}</td>
                    <td onclick="event.stopPropagation();">
                        <button class="btn-table-action" onclick="triggerExportSingle(${doc.id})" title="Export this document">📥 Export</button>
                    </td>
                </tr>
            `;
        }).join('');

        // Wire up row checkboxes
        document.querySelectorAll('.doc-checkbox').forEach(cb => {
            cb.addEventListener('change', (e) => {
                const id = parseInt(e.target.dataset.id);
                if (e.target.checked) {
                    selectedIds.add(id);
                } else {
                    selectedIds.delete(id);
                }
                renderDocuments();
                updateBulkActionsBar();
            });
        });
    }

    // Search Filter
    searchInput.addEventListener('input', renderDocuments);

    // Helper: Map badge styles
    function getFileTypeBadgeClass(type) {
        if (!type) return '';
        type = type.toLowerCase();
        if (type.includes('scanned')) return 'badge-scanned';
        if (type.includes('pdf')) return 'badge-pdf';
        if (type.includes('excel')) return 'badge-excel';
        if (type.includes('image')) return 'badge-image';
        return '';
    }

    // 4. File Upload Handler (supports multiple files)
    async function handleFileUpload(files) {
        // Convert FileList to Array if needed
        const fileArray = Array.from(files);

        // Process each file
        for (const file of fileArray) {
            // Validate extension
            const allowedExtensions = ['.pdf', '.png', '.jpg', '.jpeg', '.xlsx', '.xls'];
            const extension = file.name.substring(file.name.lastIndexOf('.')).toLowerCase();

            if (!allowedExtensions.includes(extension)) {
                showNotification(`Skipped ${file.name} - unsupported file type.`, 'warning');
                continue;
            }

            const uniqueQueueId = `queue_${Date.now()}_${Math.random()}`;

            // Add to UI upload queue visualizer
            const queueItem = document.createElement('div');
            queueItem.className = 'queue-item';
            queueItem.id = uniqueQueueId;
            queueItem.innerHTML = `
                <div class="queue-header">
                    <span class="queue-name">${escapeHtml(file.name)}</span>
                    <span id="${uniqueQueueId}_percent">0%</span>
                </div>
                <div class="queue-progress-bar">
                    <div class="queue-progress-fill" id="${uniqueQueueId}_fill"></div>
                </div>
            `;
            queueList.appendChild(queueItem);

            const formData = new FormData();
            formData.append('file', file);

            try {
                const xhr = new XMLHttpRequest();
                xhr.open('POST', '/api/document/upload', true);

                // Track upload progress
                xhr.upload.onprogress = (e) => {
                    if (e.lengthComputable) {
                        const percent = Math.round((e.loaded / e.total) * 100);
                        document.getElementById(`${uniqueQueueId}_percent`).innerText = `${percent}%`;
                        document.getElementById(`${uniqueQueueId}_fill`).style.width = `${percent}%`;
                    }
                };

                xhr.onload = async () => {
                    if (xhr.status === 200) {
                        const docRecord = JSON.parse(xhr.responseText);
                        showNotification(`Uploaded ${file.name} successfully. Extracting...`, 'success');

                        // Remove queue item after a brief delay
                        setTimeout(() => {
                            queueItem.remove();
                        }, 2000);

                        // Reload grid & start polling status
                        await loadDocuments();
                    } else {
                        throw new Error('Upload server error.');
                    }
                };

                xhr.onerror = () => {
                    throw new Error('Network error uploading file.');
                };

                xhr.send(formData);
            } catch (err) {
                console.error(err);
                showNotification(`Failed to upload ${file.name}`, 'error');
                queueItem.innerHTML = `<span style="color:#ff0844; font-weight:600;">⚠️ Upload failed: ${escapeHtml(file.name)}</span>`;
                setTimeout(() => queueItem.remove(), 5000);
            }

            // Small delay between uploads to avoid overwhelming the server
            await new Promise(resolve => setTimeout(resolve, 100));
        }

        // Clear file input so same files can be uploaded again
        fileInput.value = '';
    }

    // 5. Polling for real-time document updates
    function setupPollingForIncompleteDocs() {
        documents.forEach(doc => {
            const status = doc.status.toLowerCase();
            if ((status === 'pending' || status === 'processing') && !pollingIntervals[doc.id]) {
                _loggerInfo(`Starting real-time poll for document ID ${doc.id}`);
                
                pollingIntervals[doc.id] = setInterval(async () => {
                    try {
                        const res = await fetch(`/api/document/status/${doc.id}`);
                        if (!res.ok) return;
                        
                        const data = await res.json();
                        const currentStatus = data.status.toLowerCase();
                        
                        if (currentStatus === 'completed' || currentStatus === 'failed') {
                            _loggerInfo(`Document ID ${doc.id} finished processing with status: ${data.status}`);
                            clearInterval(pollingIntervals[doc.id]);
                            delete pollingIntervals[doc.id];
                            // Refresh full document grid
                            loadDocuments();
                        }
                    } catch (err) {
                        console.error("Polling error: ", err);
                    }
                }, 2000);
            }
        });
    }

    // Clean logging helper
    function _loggerInfo(msg) {
        console.log(`[Offline Extractor] ${msg}`);
    }

    // 6. Stats Dashboard
    function updateStats() {
        statTotal.innerText = documents.length;
        statCompleted.innerText = documents.filter(d => d.status === 'Completed').length;
        statPending.innerText = documents.filter(d => d.status === 'Pending' || d.status === 'Processing').length;
        statFailed.innerText = documents.filter(d => d.status === 'Failed').length;
    }

    // 7. Bulk Actions Bar
    function updateBulkActionsBar() {
        selectedCountSpan.innerText = selectedIds.size;
        
        if (selectedIds.size > 0) {
            bulkActionsContainer.style.display = 'flex';
        } else {
            bulkActionsContainer.style.display = 'none';
        }
    }

    // Clear Selection
    window.clearSelections = () => {
        selectedIds.clear();
        renderDocuments();
        updateBulkActionsBar();
    };

    // Bulk Delete
    window.deleteSelected = async () => {
        if (selectedIds.size === 0) return;
        
        const confirmMsg = `Are you sure you want to delete the ${selectedIds.size} selected document(s)? All database records and files on disk will be permanently removed.`;
        if (!confirm(confirmMsg)) return;

        showNotification('Deleting documents...', 'warning');

        try {
            const deletePromises = Array.from(selectedIds).map(id => 
                fetch(`/api/document/delete/${id}`, { method: 'DELETE' })
            );

            await Promise.all(deletePromises);
            
            selectedIds.clear();
            showNotification('Deleted selected files successfully.', 'success');
            await loadDocuments();
            updateBulkActionsBar();
        } catch (err) {
            console.error(err);
            showNotification('Error deleting some documents.', 'error');
        }
    };

    // Bulk Export Trigger
    window.triggerExport = (format) => {
        if (selectedIds.size === 0) {
            showNotification('Please select at least one document to export.', 'warning');
            return;
        }

        const idsArray = Array.from(selectedIds).join(',');
        exportFormatInput.value = format;
        exportIdsInput.value = idsArray;
        
        showNotification(`Generating ${format.toUpperCase()} export file...`, 'success');
        exportForm.submit();
    };

    // 8. Open Slider Drawer / Editor
    window.openDocDrawer = (id) => {
        const doc = documents.find(d => d.id === id);
        if (!doc) return;

        currentDocIdInput.value = doc.id;
        paneTitleFile.innerText = doc.fileName;

        // Raw text pane
        if (doc.rawText) {
            rawTextViewer.innerText = doc.rawText;
        } else if (doc.status === 'Pending' || doc.status === 'Processing') {
            rawTextViewer.innerHTML = `
                <div class="doc-preview-placeholder">
                    <div style="font-size: 2.5rem; animation: pulse 1s infinite;">⚙️</div>
                    <p>Document is currently being parsed by the offline extraction engine...</p>
                </div>
            `;
        } else {
            rawTextViewer.innerHTML = `
                <div class="doc-preview-placeholder">
                    <div>⚠️</div>
                    <p>No text could be extracted from this document.<br><span style="font-size:0.75rem; color:#ff0844;">Error: ${escapeHtml(doc.errorMessage || 'Unknown extraction failure')}</span></p>
                </div>
            `;
        }

        // Form Fields (Metadata)
        document.getElementById('editPoNumber').value = doc.metadata?.poNumber || '';
        document.getElementById('editVendorDetails').value = doc.metadata?.vendorDetails || '';
        document.getElementById('editDeliverTo').value = doc.metadata?.deliverTo || '';
        
        // Form Dates
        if (doc.metadata?.poDate) {
            document.getElementById('editPoDate').value = doc.metadata.poDate.substring(0, 10);
        } else {
            document.getElementById('editPoDate').value = '';
        }
        
        if (doc.metadata?.deliveryDate) {
            document.getElementById('editDeliveryDate').value = doc.metadata.deliveryDate.substring(0, 10);
        } else {
            document.getElementById('editDeliveryDate').value = '';
        }

        // Line Items Editor
        lineItemsList.innerHTML = '';
        if (doc.lineItems && doc.lineItems.length > 0) {
            doc.lineItems.forEach(item => addLineItemRow(item));
        }

        // Open Drawer View
        drawerBackdrop.classList.add('active');
        drawer.classList.add('active');
    };

    // Close Drawer View
    function closeDrawer() {
        drawerBackdrop.classList.remove('active');
        drawer.classList.remove('active');
    }

    btnCloseDrawer.addEventListener('click', closeDrawer);
    drawerBackdrop.addEventListener('click', closeDrawer);

    // 9. Add Line Item Row in UI editor
    function addLineItemRow(item = {}) {
        const row = document.createElement('div');
        row.className = 'line-item-row';
        row.innerHTML = `
            <div class="form-group item-desc">
                <input type="text" class="field-item-desc" placeholder="Item Name/Details" value="${escapeHtml(item.item || '')}" required>
            </div>
            <div class="form-group item-qty">
                <input type="number" step="any" class="field-qty" placeholder="Qty" value="${item.quantity || ''}" required>
            </div>
            <div class="form-group item-rate">
                <input type="number" step="any" class="field-rate" placeholder="Rate" value="${item.rate || ''}" required>
            </div>
            <div class="form-group item-tax-pct">
                <input type="number" step="any" class="field-tax-pct" placeholder="Tax %" value="${item.taxPercent || ''}">
            </div>
            <div class="form-group item-tax-amt">
                <input type="number" step="any" class="field-tax-amt" placeholder="Tax Amt" value="${item.taxAmount || ''}" readonly>
            </div>
            <div class="form-group item-amt">
                <input type="number" step="any" class="field-amt" placeholder="Total" value="${item.amount || ''}" required>
            </div>
            <button type="button" class="btn-remove-item" onclick="this.parentElement.remove()">🗑️</button>
        `;

        lineItemsList.appendChild(row);
        wireRowMathCalculations(row);
    }

    btnAddLineItem.addEventListener('click', () => addLineItemRow());

    // 10. Math-reactive Editor Subtotals
    function wireRowMathCalculations(row) {
        const qtyInput = row.querySelector('.field-qty');
        const rateInput = row.querySelector('.field-rate');
        const taxPctInput = row.querySelector('.field-tax-pct');
        const taxAmtInput = row.querySelector('.field-tax-amt');
        const amtInput = row.querySelector('.field-amt');

        const recalculateRow = () => {
            const qty = parseFloat(qtyInput.value) || 0;
            const rate = parseFloat(rateInput.value) || 0;
            const taxPct = parseFloat(taxPctInput.value) || 0;
            
            // Subtotal
            const subtotal = qty * rate;
            
            // Calculate Tax
            let taxAmt = 0;
            if (taxPct > 0) {
                taxAmt = subtotal * (taxPct / 100);
            }
            taxAmtInput.value = taxAmt > 0 ? taxAmt.toFixed(2) : '';

            // Calculate Grand Total for Row
            const total = subtotal;
            amtInput.value = total > 0 ? total.toFixed(2) : '';
        };

        qtyInput.addEventListener('input', recalculateRow);
        rateInput.addEventListener('input', recalculateRow);
        taxPctInput.addEventListener('input', recalculateRow);
    }

    // 11. Save Manual Changes Back to SQL Database
    drawerForm.addEventListener('submit', async (e) => {
        e.preventDefault();

        const docId = parseInt(currentDocIdInput.value);
        
        // Read edited metadata
        const poNumber = document.getElementById('editPoNumber').value;
        const vendorDetails = document.getElementById('editVendorDetails').value;
        const poDate = document.getElementById('editPoDate').value;
        const deliveryDate = document.getElementById('editDeliveryDate').value;
        const deliverTo = document.getElementById('editDeliverTo').value;

        // Read edited line items
        const lineItemRows = lineItemsList.querySelectorAll('.line-item-row');
        const lineItems = Array.from(lineItemRows).map(row => {
            return {
                item: row.querySelector('.field-item-desc').value,
                quantity: parseFloat(row.querySelector('.field-qty').value) || null,
                rate: parseFloat(row.querySelector('.field-rate').value) || null,
                taxPercent: parseFloat(row.querySelector('.field-tax-pct').value) || null,
                taxAmount: parseFloat(row.querySelector('.field-tax-amt').value) || null,
                amount: parseFloat(row.querySelector('.field-amt').value) || null
            };
        });

        const saveData = {
            documentId: docId,
            poNumber,
            vendorDetails,
            poDate: poDate || null,
            deliveryDate: deliveryDate || null,
            deliverTo,
            lineItems
        };

        showNotification('Saving changes to SQL Server...', 'warning');

        try {
            const res = await fetch('/api/document/save', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(saveData)
            });

            if (!res.ok) throw new Error('Save failed on server.');

            showNotification('Document data updated successfully in database.', 'success');
            closeDrawer();
            await loadDocuments();
        } catch (err) {
            console.error(err);
            showNotification('Error saving changes to database.', 'error');
        }
    });

    // Toast Notification helper
    function showNotification(message, type = 'success') {
        // Create element if not exists
        let container = document.getElementById('toastContainer');
        if (!container) {
            container = document.createElement('div');
            container.id = 'toastContainer';
            container.style.position = 'fixed';
            container.style.bottom = '2rem';
            container.style.right = '2rem';
            container.style.display = 'flex';
            container.style.flexDirection = 'column';
            container.style.gap = '0.5rem';
            container.style.zIndex = '9999';
            document.body.appendChild(container);
        }

        const toast = document.createElement('div');
        toast.style.background = 'rgba(18, 12, 38, 0.9)';
        toast.style.backdropFilter = 'blur(10px)';
        toast.style.padding = '0.8rem 1.5rem';
        toast.style.borderRadius = '10px';
        toast.style.fontSize = '0.85rem';
        toast.style.fontWeight = '600';
        toast.style.color = '#fff';
        toast.style.boxShadow = '0 8px 32px 0 rgba(0,0,0,0.5)';
        
        let borderStyle = '1px solid var(--panel-border)';
        if (type === 'success') borderStyle = '1px solid #00b09b';
        else if (type === 'error') borderStyle = '1px solid #ff0844';
        else if (type === 'warning') borderStyle = '1px solid #f6d365';

        toast.style.border = borderStyle;
        toast.innerText = message;

        container.appendChild(toast);

        setTimeout(() => {
            toast.style.opacity = '0';
            toast.style.transition = 'opacity 0.5s ease';
            setTimeout(() => toast.remove(), 500);
        }, 4000);
    }

    // Export Single Document
    window.triggerExportSingle = (id) => {
        const selectedSet = new Set([id]);
        const idsArray = Array.from(selectedSet).join(',');

        // Trigger Excel export by default
        exportFormatInput.value = 'excel';
        exportIdsInput.value = idsArray;

        showNotification('Generating Excel export file...', 'success');
        exportForm.submit();
    }

    // HTML Escaper
    function escapeHtml(text) {
        if (!text) return '';
        return text
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#039;");
    }
});
