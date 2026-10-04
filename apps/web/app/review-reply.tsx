import type {ProviderReply} from "../lib/review-client";

export default function ReviewReply({reply}:{reply:ProviderReply|null|undefined}){
 if(!reply)return null;
 return <section className="provider-response" aria-label="Provider response"><div className="provider-response-heading"><strong>Provider response</strong><time dateTime={reply.createdAt}>{new Date(reply.createdAt).toISOString().slice(0,10)} UTC</time></div><p className="review-comment">{reply.comment}</p></section>;
}
