import { generateInvoice } from './ksef-fe-invoice-converter.js';

window.renderInvoicePdf = (xmlBase64, nrKSeF, qrCode) => generateInvoice(
  new File([Uint8Array.from(atob(xmlBase64), (c) => c.charCodeAt(0))], 'invoice.xml', { type: 'application/xml' }),
  { nrKSeF, qrCode },
  'base64');

window.HybridWebView.SendRawMessage('ready');
